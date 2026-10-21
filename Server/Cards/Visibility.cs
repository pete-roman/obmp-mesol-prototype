using System;

namespace MESOL.Server.Cards;

/// <summary>
/// Who may see a field, and how far a viewer is towards a card's owner. Ordered: each level includes the ones
/// before it, so a viewer sees a field when <c>viewerLevel &gt;= field visibility</c>.
/// </summary>
public enum Visibility : byte
{
    /// <summary>Everyone on the server.</summary>
    Public = 0,

    /// <summary>Players the owner introduced themselves to (one-sided, owner to viewer).</summary>
    Known = 1,

    /// <summary>Mutual friends.</summary>
    Friends = 2,

    /// <summary>Only the owner.</summary>
    Private = 3,
}

public static class ViewerLevel
{
    /// <summary>The level a viewer has towards a card's owner.</summary>
    public static Visibility Of(bool isOwner, bool isFriend, bool isKnown)
    {
        if (isOwner)
        {
            return Visibility.Private;
        }
        if (isFriend)
        {
            return Visibility.Friends;
        }
        return isKnown ? Visibility.Known : Visibility.Public;
    }

    public static bool TryParse(string text, out Visibility visibility)
    {
        switch ((text ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "public":
                visibility = Visibility.Public;
                return true;
            case "known":
                visibility = Visibility.Known;
                return true;
            case "friends":
            case "friend":
                visibility = Visibility.Friends;
                return true;
            case "private":
                visibility = Visibility.Private;
                return true;
            default:
                visibility = Visibility.Public;
                return false;
        }
    }

    public static string Word(Visibility visibility) => visibility switch
    {
        Visibility.Public => "Public",
        Visibility.Known => "Known",
        Visibility.Friends => "Friends",
        Visibility.Private => "Private",
        _ => throw new ArgumentOutOfRangeException(nameof(visibility)),
    };
}
