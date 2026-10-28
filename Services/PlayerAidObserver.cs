namespace RoAura.Services;

/// <summary>
/// Identifies this client actor independently of Character ID (CID).
/// Some map proxies expose an already-connected TCP stream or omit a
/// recognizable map-login, but the server still sends ZC_AID (0x0283)
/// or an own-character EXP packet. No Status/Option or arbitrary cast may
/// establish self identity. This never routes character statistics.
/// </summary>
public sealed class PlayerAidObserver
{
    private readonly object _sync = new();
    private CharacterSessionFlow? _flow;
    private uint _aid;
    private string _source = "";
    private bool _conflicted;

    public uint Aid { get { lock (_sync) return _conflicted ? 0 : _aid; } }
    public string Source { get { lock (_sync) return _conflicted ? "conflict" : _source; } }

    public uint AidForFlow(CharacterSessionFlow flow)
    {
        lock (_sync) return _flow == flow && !_conflicted ? _aid : 0;
    }

    public string SourceForFlow(CharacterSessionFlow flow)
    {
        lock (_sync) return _flow == flow ? (_conflicted ? "conflict" : _source) : "";
    }

    public void Reset()
    {
        lock (_sync)
        {
            _flow = null;
            _aid = 0;
            _source = "";
            _conflicted = false;
        }
    }

    /// <returns>True if a fresh, nonzero self ID was learned.</returns>
    public bool Observe(CharacterSessionFlow flow, uint aid, string source)
    {
        if (aid == 0 || source is not ("ZC_AID" or "EXP")) return false;
        lock (_sync)
        {
            if (_flow != flow)
            {
                _flow = flow;
                _aid = 0;
                _source = "";
                _conflicted = false;
            }
            if (_conflicted) return false;
            if (_aid != 0 && _aid != aid)
            {
                // One flow identifying two different actors is unsafe.
                _aid = 0;
                _source = "";
                _conflicted = true;
                return false;
            }
            if (_aid == aid) return false;
            _aid = aid;
            _source = source;
            return true;
        }
    }

    public void OnClosed(CharacterSessionFlow flow)
    {
        lock (_sync)
        {
            if (_flow != flow) return;
            _flow = null;
            _aid = 0;
            _source = "";
            _conflicted = false;
        }
    }
}
