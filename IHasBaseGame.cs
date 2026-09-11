namespace SendGameStatusPlugin;

/// <summary>
/// 任何拥有 <c>baseGame</c> 字段的 payload 都需要实现此接口，
/// 使 GameStatusOutput 能强类型访问 single_mode_chara_id。
/// </summary>
internal interface IHasBaseGame
{
    GameStatusSend_Base<PersonBase> baseGame { get; }
}
