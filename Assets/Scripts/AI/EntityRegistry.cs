using System.Collections.Generic;
using UnityEngine;

public static class EntityRegistry
{
    private static readonly HashSet<NetworkPlayerSync> _players = new HashSet<NetworkPlayerSync>();
    private static readonly HashSet<AIPlayerMovement> _bots = new HashSet<AIPlayerMovement>();
    private static readonly HashSet<JellyObject> _jellies = new HashSet<JellyObject>();

    public static IReadOnlyCollection<NetworkPlayerSync> Players => _players;
    public static IReadOnlyCollection<AIPlayerMovement> Bots => _bots;
    public static IReadOnlyCollection<JellyObject> Jellies => _jellies;

    public static void Register(NetworkPlayerSync p) => _players.Add(p);
    public static void Unregister(NetworkPlayerSync p) => _players.Remove(p);

    public static void Register(AIPlayerMovement b) => _bots.Add(b);
    public static void Unregister(AIPlayerMovement b) => _bots.Remove(b);

    public static void Register(JellyObject j) => _jellies.Add(j);
    public static void Unregister(JellyObject j) => _jellies.Remove(j);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear()
    {
        _players.Clear();
        _bots.Clear();
        _jellies.Clear();
    }
}
