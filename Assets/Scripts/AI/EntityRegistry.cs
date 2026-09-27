using System.Collections.Generic;
using UnityEngine;
using JellyNet;

public static class EntityRegistry
{
    private static readonly List<LanPlayerState> players = new List<LanPlayerState>();
    private static readonly List<JellyObject> jellies = new List<JellyObject>();

    private static List<LanPlayerState> playersSnapshot = new List<LanPlayerState>();
    private static List<JellyObject> jelliesSnapshot = new List<JellyObject>();

    private static bool playersDirty = true;
    private static bool jelliesDirty = true;

    private static readonly List<INetEntity> entities = new List<INetEntity>();
    private static List<INetEntity> entitiesSnapshot = new List<INetEntity>();
    private static bool entitiesDirty = true;

    public static IReadOnlyList<LanPlayerState> Players
    {
        get
        {
            if (playersDirty)
            {
                playersSnapshot = new List<LanPlayerState>(players);
                playersDirty = false;
            }
            return playersSnapshot;
        }
    }

    public static IReadOnlyList<JellyObject> Jellies
    {
        get
        {
            if (jelliesDirty)
            {
                jelliesSnapshot = new List<JellyObject>(jellies);
                jelliesDirty = false;
            }
            return jelliesSnapshot;
        }
    }

    public static IReadOnlyList<INetEntity> Entities
    {
        get
        {
            if (entitiesDirty)
            {
                entitiesSnapshot = new List<INetEntity>(entities);
                entitiesDirty = false;
            }
            return entitiesSnapshot;
        }
    }

    public static void Register(INetEntity e)
    {
        entities.Add(e);
        entitiesDirty = true;
    }

    public static void Unregister(INetEntity e)
    {
        if (entities.Remove(e))
            entitiesDirty = true;
    }

    public static void Register(LanPlayerState p)
    {
        players.Add(p);
        playersDirty = true;
        Register((INetEntity)p);
    }

    public static void Unregister(LanPlayerState p)
    {
        if (players.Remove(p))
            playersDirty = true;
        Unregister((INetEntity)p);
    }

    public static void Register(JellyObject j)
    {
        jellies.Add(j);
        jelliesDirty = true;
    }

    public static void Unregister(JellyObject j)
    {
        if (jellies.Remove(j))
            jelliesDirty = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear()
    {
        players.Clear();
        jellies.Clear();
        entities.Clear();
        playersDirty = true;
        jelliesDirty = true;
        entitiesDirty = true;
    }
}
