using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace Holm.Platform.Linux;

/// <summary>
/// One D-Bus connection (the session's or the system's), opened the first time it is needed and opened again if
/// it is lost, with the few calls the backends make. Everything waits on signals: nothing here polls.
/// </summary>
sealed class Bus
{
    public static readonly Bus Session = new(() => DBusAddress.Session);
    public static readonly Bus System = new(() => DBusAddress.System);

    const string Properties = "org.freedesktop.DBus.Properties";

    readonly Func<string?> _address;
    readonly object _gate = new();
    Task<DBusConnection?>? _connecting;

    Bus(Func<string?> address) => _address = address;

    /// <summary>The connection, or null when this system has no such bus (or it can't be reached).</summary>
    public Task<DBusConnection?> ConnectAsync()
    {
        lock (_gate) return _connecting ??= OpenAsync();
    }

    async Task<DBusConnection?> OpenAsync()
    {
        try
        {
            if (_address() is not { Length: > 0 } address) return null;
            var connection = new DBusConnection(address);
            await connection.ConnectAsync().ConfigureAwait(false);
            // If the bus goes away, the next caller connects afresh.
            _ = connection.DisconnectedAsync().ContinueWith(_ => { lock (_gate) _connecting = null; }, TaskScheduler.Default);
            return connection;
        }
        catch
        {
            lock (_gate) _connecting = null;
            return null;
        }
    }

    /// <summary>All properties of one interface of an object; empty when the object or the bus isn't there.</summary>
    public async Task<Dictionary<string, VariantValue>> GetAllAsync(string service, string path, string @interface)
    {
        try
        {
            if (await ConnectAsync().ConfigureAwait(false) is not { } connection) return new();
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(service, path, Properties, "GetAll", "s");
                writer.WriteString(@interface);
                message = writer.CreateMessage();
            }
            return await connection.CallMethodAsync(message, static (Message m, object? _) => m.GetBodyReader().ReadDictionaryOfStringToVariantValue(), null).ConfigureAwait(false);
        }
        catch { return new(); }
    }

    /// <summary>One property; null when it can't be read.</summary>
    public async Task<VariantValue?> GetAsync(string service, string path, string @interface, string property)
    {
        try
        {
            if (await ConnectAsync().ConfigureAwait(false) is not { } connection) return null;
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(service, path, Properties, "Get", "ss");
                writer.WriteString(@interface);
                writer.WriteString(property);
                message = writer.CreateMessage();
            }
            return await connection.CallMethodAsync(message, static (Message m, object? _) => m.GetBodyReader().ReadVariantValue(), null).ConfigureAwait(false);
        }
        catch { return null; }
    }

    /// <summary>Sets a property. Failures are the player's business (it may not allow it) and are ignored.</summary>
    public async Task SetAsync(string service, string path, string @interface, string property, VariantValue value)
    {
        try
        {
            if (await ConnectAsync().ConfigureAwait(false) is not { } connection) return;
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(service, path, Properties, "Set", "ssv");
                writer.WriteString(@interface);
                writer.WriteString(property);
                writer.WriteVariant(value);
                message = writer.CreateMessage();
            }
            await connection.CallMethodAsync(message).ConfigureAwait(false);
        }
        catch { }
    }

    /// <summary>Calls a method; <paramref name="write"/> adds the arguments its <paramref name="signature"/> names.</summary>
    public async Task<bool> CallAsync(string service, string path, string @interface, string method, string? signature = null, Action<MessageWriterBox>? write = null)
    {
        try
        {
            if (await ConnectAsync().ConfigureAwait(false) is not { } connection) return false;
            MessageBuffer message;
            var writer = connection.GetMessageWriter();
            try
            {
                writer.WriteMethodCallHeader(service, path, @interface, method, signature);
                if (write != null)
                {
                    var box = new MessageWriterBox();
                    write(box);
                    foreach (var step in box.Steps) step(ref writer);
                }
                message = writer.CreateMessage();
            }
            finally { writer.Dispose(); }
            await connection.CallMethodAsync(message).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Calls a method that takes nothing and reads its answer; default when it fails.</summary>
    public async Task<T?> CallAsync<T>(string service, string path, string @interface, string method, MessageValueReader<T> read)
    {
        try
        {
            if (await ConnectAsync().ConfigureAwait(false) is not { } connection) return default;
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(service, path, @interface, method);
                message = writer.CreateMessage();
            }
            return await connection.CallMethodAsync(message, read, null).ConfigureAwait(false);
        }
        catch { return default; }
    }

    /// <summary>Calls <paramref name="handler"/> (on a thread of the connection) for every signal the rule matches.</summary>
    public async Task<IDisposable?> WatchAsync(MatchRule rule, Action<SignalInfo> handler)
    {
        try
        {
            if (await ConnectAsync().ConfigureAwait(false) is not { } connection) return null;
            return await connection.AddMatchAsync(rule,
                static (Message m, object? _) => new SignalInfo(m.SenderAsString ?? "", m.PathAsString ?? "", m.InterfaceAsString ?? "", m.MemberAsString ?? "", FirstString(m)),
                (Notification<SignalInfo> notification) => { if (notification.HasValue) handler(notification.Value); },
                false, ObserverFlags.None).ConfigureAwait(false);
        }
        catch { return null; }
    }

    /// <summary>The first argument when it is a string (the interface name of PropertiesChanged, the name of NameOwnerChanged).</summary>
    static string FirstString(Message message)
    {
        var signature = message.SignatureAsString;
        if (signature == null || signature.Length == 0 || signature[0] != 's') return "";
        try { return message.GetBodyReader().ReadString(); } catch { return ""; }
    }
}

/// <summary>Which signal arrived, from whom, and its first string argument.</summary>
readonly record struct SignalInfo(string Sender, string Path, string Interface, string Member, string Arg0);

/// <summary>
/// Gathers a call's arguments. A MessageWriter is a ref struct and can't be captured by a lambda, so the steps
/// are collected here and replayed on it (by reference: a copy would write past the original's position).
/// </summary>
sealed class MessageWriterBox
{
    internal readonly List<WriteStep> Steps = new();
    internal delegate void WriteStep(ref MessageWriter writer);

    public void String(string value) => Steps.Add((ref MessageWriter w) => w.WriteString(value));
    public void ObjectPath(string value) => Steps.Add((ref MessageWriter w) => w.WriteObjectPath(value));
    public void Int64(long value) => Steps.Add((ref MessageWriter w) => w.WriteInt64(value));
}
