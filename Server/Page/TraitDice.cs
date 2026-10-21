using ReadyM.Api.Idents;
using ReadyM.SDK.Attributes;
using RpRoll.Server;

namespace MESOL.Server.Page;

/// <summary>
/// A trait roll: 5d20 through RpRoll, so it is a real server roll that players within 20 m see in chat
/// ("Pete rolled 5d20 for Lazy: ..."), and the player sees the dice land on a small board.
/// </summary>
[Service]
public sealed partial class TraitDice(RpRollApi rpRoll)
{
    public int[] Roll(PlayerId player, string traitLabel)
    {
        return rpRoll.Roll(player, 0, 0, Cards.CardFields.TraitDice, 0, traitLabel, showOnBoard: true).Values;
    }
}
