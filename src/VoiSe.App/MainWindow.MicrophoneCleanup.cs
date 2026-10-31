using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VoiSe.Audio;

namespace VoiSe.App;

public sealed partial class MainWindow
{
    private bool _cleanupLoading = true;
    private void InitializeMicrophoneCleanup()
    {
        EchoCancellationSwitch.IsOn = _settings.EchoCancellationEnabled;
        RoomEchoReductionSwitch.IsOn = _settings.RoomEchoReductionEnabled;
        _cleanupLoading = false;
    }
    private string? SelectedEchoReferenceId => (EchoReferenceComboBox.SelectedItem as ComboBoxItem)?.Tag as string is { Length: > 0 } id ? id : null;
    private void FillEchoReferenceDevices(IReadOnlyList<AudioDeviceInfo> devices)
    {
        var selectedId = SelectedEchoReferenceId ?? _settings.EchoReferenceDeviceId;
        _cleanupLoading = true;
        EchoReferenceComboBox.Items.Clear();
        var automatic = new ComboBoxItem { Content = "Устройство Windows по умолчанию", Tag = "" };
        EchoReferenceComboBox.Items.Add(automatic);
        ComboBoxItem selected = automatic;
        foreach (var device in devices.Where(d => !IsLikelyVBCableDevice(d)))
        {
            var item = new ComboBoxItem { Content = device.FriendlyName, Tag = device.Id };
            EchoReferenceComboBox.Items.Add(item); if (device.Id == selectedId) selected = item;
        }
        if (selectedId is not null && ReferenceEquals(selected, automatic))
        {
            selected = new ComboBoxItem { Content = "Ранее выбранное устройство отключено", Tag = selectedId };
            EchoReferenceComboBox.Items.Add(selected);
        }
        EchoReferenceComboBox.SelectedItem = selected;
        _cleanupLoading = false;
    }
    private void OnEchoCleanupChanged(object sender, RoutedEventArgs e) => ApplyMicrophoneCleanupChange();
    private void OnEchoReferenceChanged(object sender, SelectionChangedEventArgs e) => ApplyMicrophoneCleanupChange();
    private void ApplyMicrophoneCleanupChange()
    {
        if (_loadingSettings || _cleanupLoading) return;
        SaveCurrentSettings(); UpdateMicrophoneCleanupStatus();
        if (_engine is not null) ScheduleEngineRestart("microphone echo cleanup changed");
    }
    private void OnEchoRefreshDevices(object sender, RoutedEventArgs e)
    {
        OnRefreshDevicesClick(sender, e);
        if (_engine is not null && EchoCancellationSwitch.IsOn) ScheduleEngineRestart("echo reference refreshed");
    }
    private void UpdateMicrophoneCleanupStatus()
    {
        EchoReferenceComboBox.IsEnabled = EchoCancellationSwitch.IsOn;
        if (!EchoCancellationSwitch.IsOn && !RoomEchoReductionSwitch.IsOn)
        { EchoCleanupStatus.Text = "Удаление эха выключено."; return; }
        if (_engine is null)
        { EchoCleanupStatus.Text = "Настройки сохранены. Обработка включится после запуска микрофона."; return; }
        var speaker = !EchoCancellationSwitch.IsOn ? "" : _engine.EchoCancellationAvailable
            ? $"Звук колонок: обработка включена ({_engine.EchoReferenceDeviceName})."
            : "Звук колонок: недоступно. " + (_engine.EchoCancellationError ?? "Ожидание запуска.");
        EchoCleanupStatus.Text = speaker + (RoomEchoReductionSwitch.IsOn ? " Эхо комнаты: ослабление включено." : "");
    }
}
