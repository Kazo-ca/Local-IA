using CommunityToolkit.Mvvm.Messaging.Messages;
using LocalIA.Core.Models;

namespace LocalIA.Core.Messaging;

public sealed class HardwareSnapshotChangedMessage(HardwareSnapshot snapshot) : ValueChangedMessage<HardwareSnapshot>(snapshot);
