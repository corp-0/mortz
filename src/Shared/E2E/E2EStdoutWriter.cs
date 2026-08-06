#if TOOLS
using System.Text;
using Mortz.E2E.Protocol;

namespace Mortz.Shared.E2E;

/// <summary>The single owner of structured stdout: it skips the log sinks
/// because the Godot C++ logger and Serilog's console sink share the OS
/// handle and a line could shear. The harness counts sheared lines and
/// carries on.</summary>
public sealed class E2EStdoutWriter : IE2EResponder
{
    private readonly object _gate = new();
    private readonly StreamWriter _out;

    public E2EStdoutWriter()
    {
        _out = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
    }

    public void Respond(E2EResponse response) =>
        Write(E2EWire.Serialize(new ResponseMessage(response)));

    public void Emit(E2EEvent evt) =>
        Write(E2EWire.Serialize(new EventMessage(evt)));

    public void Flush()
    {
        lock (_gate)
        {
            _out.Flush();
        }
    }

    private void Write(string json)
    {
        lock (_gate)
        {
            _out.Write(E2EWire.STDOUT_PREFIX + json + "\n");
        }
    }
}
#endif
