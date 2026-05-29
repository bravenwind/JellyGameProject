using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 존재하는 플레이어/봇/젤리를 중앙에서 관리하는 정적 레지스트리.
///
/// [순회 안전성 설계]
///   내부 저장은 HashSet(중복 방지 + 빠른 추가/삭제)을 사용하지만,
///   HashSet은 foreach 순회 도중 Add/Remove가 일어나면 InvalidOperationException을 던진다.
///   멀티플레이에서 한 프레임에 여러 엔티티가 파괴되면(OnDisable → Unregister)
///   AI 탐지 루프 순회 중에 실제로 터질 수 있다.
///
///   이를 막기 위해 외부에는 "스냅샷 List"를 노출한다.
///   - Register/Unregister 시 dirty 플래그만 세운다 (스냅샷은 건드리지 않음).
///   - 외부에서 컬렉션에 접근할 때, dirty면 새 List를 1회 생성해 캐싱한다.
///   - 새 List 인스턴스를 만들기 때문에, 순회 도중 엔티티가 사라져도
///     이미 진행 중인 foreach는 옛 스냅샷을 그대로 돌아 안전하다.
///   변경이 없으면 스냅샷을 재사용하므로 매 프레임 GC 할당도 발생하지 않는다.
/// </summary>
public static class EntityRegistry
{
    private static readonly HashSet<NetworkPlayerSync> _players = new HashSet<NetworkPlayerSync>();
    private static readonly HashSet<AIPlayerMovement> _bots = new HashSet<AIPlayerMovement>();
    private static readonly HashSet<JellyObject> _jellies = new HashSet<JellyObject>();

    // 외부 순회용 스냅샷 (dirty일 때만 재생성)
    private static List<NetworkPlayerSync> _playersSnapshot = new List<NetworkPlayerSync>();
    private static List<AIPlayerMovement> _botsSnapshot = new List<AIPlayerMovement>();
    private static List<JellyObject> _jelliesSnapshot = new List<JellyObject>();

    private static bool _playersDirty = true;
    private static bool _botsDirty = true;
    private static bool _jelliesDirty = true;

    public static IReadOnlyList<NetworkPlayerSync> Players
    {
        get
        {
            if (_playersDirty)
            {
                _playersSnapshot = new List<NetworkPlayerSync>(_players);
                _playersDirty = false;
            }
            return _playersSnapshot;
        }
    }

    public static IReadOnlyList<AIPlayerMovement> Bots
    {
        get
        {
            if (_botsDirty)
            {
                _botsSnapshot = new List<AIPlayerMovement>(_bots);
                _botsDirty = false;
            }
            return _botsSnapshot;
        }
    }

    public static IReadOnlyList<JellyObject> Jellies
    {
        get
        {
            if (_jelliesDirty)
            {
                _jelliesSnapshot = new List<JellyObject>(_jellies);
                _jelliesDirty = false;
            }
            return _jelliesSnapshot;
        }
    }

    public static void Register(NetworkPlayerSync p) { if (_players.Add(p)) _playersDirty = true; }
    public static void Unregister(NetworkPlayerSync p) { if (_players.Remove(p)) _playersDirty = true; }

    public static void Register(AIPlayerMovement b) { if (_bots.Add(b)) _botsDirty = true; }
    public static void Unregister(AIPlayerMovement b) { if (_bots.Remove(b)) _botsDirty = true; }

    public static void Register(JellyObject j) { if (_jellies.Add(j)) _jelliesDirty = true; }
    public static void Unregister(JellyObject j) { if (_jellies.Remove(j)) _jelliesDirty = true; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear()
    {
        _players.Clear();
        _bots.Clear();
        _jellies.Clear();
        _playersDirty = true;
        _botsDirty = true;
        _jelliesDirty = true;
    }
}
