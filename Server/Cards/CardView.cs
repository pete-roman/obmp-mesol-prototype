using System.Collections.Generic;

namespace MESOL.Server.Cards;

/// <summary>
/// The privacy filter: what of a card a viewer at a given level may see. Everything MESOL shows to another player
/// goes through here, on the server, so a client never receives a field it may not see.
/// </summary>
public static class CardView
{
    /// <summary>The fields with content that a viewer at <paramref name="level"/> may see, in card order.</summary>
    public static IEnumerable<(FieldDefinition Field, FieldValue Value)> Visible(CardFile card, Visibility level)
    {
        foreach (FieldDefinition field in CardFields.All)
        {
            if (card.Get(field) is { IsEmpty: false } value && EffectiveVisibility(field, value) <= level)
            {
                yield return (field, value);
            }
        }
    }

    public static bool CanSee(CardFile card, FieldDefinition field, Visibility level)
    {
        return card.Get(field) is { IsEmpty: false } value && EffectiveVisibility(field, value) <= level;
    }

    /// <summary>A locked field always has its fixed visibility, whatever the file says.</summary>
    public static Visibility EffectiveVisibility(FieldDefinition field, FieldValue value)
    {
        return field.VisibilityLocked ? field.DefaultVisibility : value.Visibility;
    }
}
