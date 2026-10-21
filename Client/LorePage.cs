using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MESOL.Common;
using Microsoft.Extensions.Logging;
using OblivionMp.ChatMod;
using OblivionMp.Sdk;
using ReadyM.Cef;
using ReadyM.Cef.Rpc;
using ReadyM.SDK.Attributes;
using ReadyM.Sdk.Common.Input;

namespace MESOL.Client;

/// <summary>
/// The card page: a web page (web\) with the player's own card to fill in, or another player's card to read.
/// The page asks, the server answers (<see cref="MesolRpc"/>); this class only carries the texts and shows or hides
/// the page. Nothing here decides what a card may hold or who sees it.
///
/// Keys. End (Polled, so it also closes the page while the page holds the keyboard) shows or hides the editor;
/// /mylore and /lore open it from chat. RpRoll keeps Home. While the player types in one of the page's boxes, the page
/// says so (<see cref="OnMesolTyping"/>) and this class sets <see cref="TextInputKey"/> in the AppDomain: End and
/// RpRoll's Home then move the caret instead of closing or opening anything. While the page is open it holds the keyboard and the game is in UI-only
/// input (<see cref="CefHost.SetUiInputMode"/>), so typing a name never moves the character. Escape on the page
/// closes it, K on the page opens chat, and the keyboard comes back to the page when chat closes.
///
/// Game to page: the state (open or not, and the server's last own message) as one JSON message, sent on every change
/// and again a few times after it until the page confirms it; and each answer once, by request id (the page asks
/// again when an answer does not come).
///
/// Meeting players (M2): = offers an introduction to the player in front, or accepts the newest offer; - declines it;
/// Shift+End opens the card of the player in front. Offers and notes show on the offer panel (panel.html), a second,
/// transparent viewport at the top of the screen that never takes the keyboard, so the player can keep walking.
/// While the card is open these keys are the page's (its Nearby section), and none of them fire while typing.
/// </summary>
[Service]
public sealed partial class LorePage(CefHost cef, IInput input, IChat chat, MesolClientRpc rpc, ILogger logger)
{
    private const string Host = "mesol";

    // Above chat's bar (0); RpRoll's board is 7, above this page.
    private const int ZOrder = 6;

    // The offer panel, above everything: it only ever shows a small box at the top of the screen.
    private const int PanelZOrder = 8;

    /// <summary>AppDomain data slot that is <c>true</c> while a mod's page has a text box with the keyboard (RpRoll reads it).</summary>
    public const string TextInputKey = "OBMP.TextInputActive";

    /// <summary>AppDomain data slot RpRoll sets while its dice board is open.</summary>
    private const string BoardOpenKey = "RpRoll.BoardOpen";

    // Resends after a change, in seconds. Covers a page that is still loading and messages that got lost.
    private static readonly double[] Resends = { 0.25, 0.75, 2.0, 4.0 };

    // A request from the page bigger than this is not sent (the server would refuse it anyway).
    private const int MaxRequestLength = 32_000;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IDisposable? _keyBind;
    private IDisposable? _introduceBind;
    private IDisposable? _declineBind;
    private IDisposable? _lookBind;
    private ViewportHandle _viewport;
    private BrowserHandle? _page;
    private ViewportHandle _panelViewport;
    private BrowserHandle? _panel;
    private bool _panelShown;
    private double _toggleAt = -1;
    private double _lookAt = -10;

    private bool _open;
    private bool _pageReady;
    private bool _chatWasOpen;
    private long _seq;
    private long _pushId;
    private string? _push;
    private double _changedAt;
    private int _resendIndex = Resends.Length;
    private double _cursorHiddenAt;
    private double _refocusedAt;
    private bool _refocusLogged;

    private double Now => _clock.Elapsed.TotalSeconds;

    private void Start()
    {
        string folder = Path.GetDirectoryName(typeof(LorePage).Assembly.Location) ?? AppContext.BaseDirectory;
        cef.RegisterFolder(Host, Path.Combine(folder, "web"));
        rpc.Answered += OnAnswered;

        _keyBind = input.RegisterKeyBind(Key.END, () => _toggleAt = Now, KeySource.Polled);
        _lookBind = input.RegisterKeyBind(ModifierKeys.Shift, Key.END, () => Gesture("look"), KeySource.Polled);
        _introduceBind = input.RegisterKeyBind(Key.OEM_PLUS, () => Gesture("introduce"), KeySource.Polled);
        _declineBind = input.RegisterKeyBind(Key.OEM_MINUS, () => Gesture("decline"), KeySource.Polled);

        // The offer panel: full screen and transparent, hidden until there is something to show.
        _panelViewport = cef.CreateViewport(PanelZOrder);
        cef.SetPlacement(_panelViewport, ViewportPlacement.FullScreen);
        CefBrowserInstance panel = cef.CreateBrowser();
        _panel = panel.Handle;
        panel.Navigate($"readym://{Host}/panel.html");
        cef.Assign(_panelViewport, panel.Handle);
        cef.SetVisible(_panelViewport, false);

        // Created hidden right away, so the page has loaded long before it is first opened.
        _viewport = cef.CreateViewport(ZOrder);
        cef.SetPlacement(_viewport, ViewportPlacement.FullScreen);
        CefBrowserInstance browser = cef.CreateBrowser();
        _page = browser.Handle;
        browser.Navigate($"readym://{Host}/index.html");
        cef.Assign(_viewport, browser.Handle);
        cef.SetVisible(_viewport, false);
        Changed();
        logger.LogInformation("[MESOL] Registered: End (or /mylore) opens your lore card, Shift+End the card of the player in front, = introduces or accepts, - declines (readym://{Host}/).", Host);
    }

    private void Stop()
    {
        rpc.Answered -= OnAnswered;
        _keyBind?.Dispose();
        _introduceBind?.Dispose();
        _declineBind?.Dispose();
        _lookBind?.Dispose();
        SetTyping(false);
    }

    private void Update()
    {
        // End waits one frame: if Shift+End fired with it, that was a look at someone, not the card.
        if (_toggleAt >= 0 && Now - _toggleAt >= 0.05)
        {
            bool look = _lookAt >= _toggleAt - 0.25;
            _toggleAt = -1;
            if (!look)
            {
                Toggle();
            }
        }
        // Chat closed while the page is open: the keyboard goes back to the page.
        bool chatOpen = chat.IsOpen;
        if (_chatWasOpen && !chatOpen && _open)
        {
            Guard("back to the card", () => cef.SetFocused(_viewport, true));
        }
        _chatWasOpen = chatOpen;
        // UI-only input brings the cursor back now and then; the page takes no mouse, so keep hiding it.
        if (_open && !chatOpen && Now - _cursorHiddenAt > 0.25)
        {
            _cursorHiddenAt = Now;
            Guard("hiding the cursor", () => cef.SetCursorVisible(false));
        }
        if (_resendIndex < Resends.Length && Now - _changedAt >= Resends[_resendIndex])
        {
            _resendIndex++;
            PushState();
        }
    }

    private void Toggle()
    {
        Guard("End", () =>
        {
            if (chat.IsOpen || AppDomain.CurrentDomain.GetData(TextInputKey) is true)
            {
                return; // typing in chat or in a text box: End moves the caret there
            }
            if (!_open && AppDomain.CurrentDomain.GetData(BoardOpenKey) is true)
            {
                return; // RpRoll's board has the keyboard; the card would open underneath it
            }
            if (_open)
            {
                SetOpen(false);
                return;
            }
            Show("{\"op\":\"edit\"}");
        });
    }

    /// <summary>Opens the page with a message for it: the editor, or another player's card.</summary>
    private void Show(string push)
    {
        if (chat.IsOpen)
        {
            chat.Close(); // the page takes the keyboard; chat opens again with K
        }
        _push = push;
        _pushId++;
        SetOpen(true);
    }

    /// <summary>=, - and Shift+End in the game. Not while chat or a text box has the keyboard, nor while the card is open.</summary>
    private void Gesture(string what)
    {
        if (what == "look")
        {
            _lookAt = Now;
        }
        Guard(what, () =>
        {
            if (chat.IsOpen || _open || AppDomain.CurrentDomain.GetData(TextInputKey) is true
                || AppDomain.CurrentDomain.GetData(BoardOpenKey) is true)
            {
                return;
            }
            rpc.SendGesture(what);
        });
    }

    private void OnAnswered(long requestId, string json)
    {
        Guard("an answer from the server", () =>
        {
            if (requestId == -1)
            {
                // An offer, its end, or a note: on the panel, and on the card page so its Nearby section knows.
                if (_panel is BrowserHandle panel)
                {
                    if (!_panelShown)
                    {
                        _panelShown = true;
                        cef.SetVisible(_panelViewport, true);
                    }
                    SendMesolPanel(panel, json);
                }
                if (_page is BrowserHandle card)
                {
                    SendMesolPanel(card, json);
                }
                return;
            }
            if (requestId == 0)
            {
                Show(json); // /mylore or /lore
                return;
            }
            if (_page is BrowserHandle page)
            {
                SendMesolAnswer(page, requestId, json);
            }
        });
    }

    private void SetOpen(bool open)
    {
        _open = open;
        if (!open)
        {
            SetTyping(false);
        }
        cef.SetVisible(_viewport, open);
        // The page takes the keyboard while it is open, and the game gets no input at all (UI-only), so letters typed
        // into a field never reach the game. End is Polled, so closing never depends on the page.
        cef.SetFocused(_viewport, open);
        cef.SetUiInputMode(open);
        if (open)
        {
            // UI-only input shows the mouse cursor, but pages get no mouse in OB:MP 1.0; a click only took the keyboard
            // away from the page, and the card seemed to hang until End twice. Keys only, so no cursor.
            cef.SetCursorVisible(false);
        }
        Changed();
    }

    private void Changed()
    {
        _seq++;
        _changedAt = Now;
        _resendIndex = 0;
        PushState();
    }

    private void PushState()
    {
        if (_page is not BrowserHandle page)
        {
            return;
        }
        try
        {
            string json = JsonSerializer.Serialize(new { seq = _seq, open = _open, pushId = _pushId, push = _push });
            SendMesolState(page, json);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[MESOL] Sending the state to the page failed.");
        }
    }

    private void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[MESOL] {What} failed.", what);
        }
    }

    // ---- game to page ----

    [SendToCef]
    public partial void SendMesolState(BrowserHandle page, string json);

    [SendToCef]
    public partial void SendMesolAnswer(BrowserHandle page, long id, string json);

    /// <summary>An offer panel message (offer, clear, note), to the panel and to the card page.</summary>
    [SendToCef]
    public partial void SendMesolPanel(BrowserHandle page, string json);

    // ---- page to game ----

    [ReceiveFromCef]
    private void OnMesolReady(BrowserHandle page)
    {
        logger.LogInformation("[MESOL] Card page loaded and talking back.");
        PushState();
        if (!_pageReady)
        {
            _pageReady = true;
            Guard("telling the server the page is ready", () => rpc.SendPageReady());
        }
    }

    [ReceiveFromCef]
    private void OnMesolSeen(BrowserHandle page, long seq)
    {
        if (seq >= _seq)
        {
            _resendIndex = Resends.Length; // the page has the latest state, no need to repeat it
        }
    }

    /// <summary>A request from the page (JSON with an "op"), passed to the server in pieces.</summary>
    [ReceiveFromCef]
    private void OnMesolAsk(BrowserHandle page, long id, string json)
    {
        Guard("a request from the page", () =>
        {
            if (id <= 0 || string.IsNullOrEmpty(json) || json.Length > MaxRequestLength)
            {
                logger.LogWarning("[MESOL] Ignored a request from the page (#{Id}, {Length} characters).", id, json?.Length ?? 0);
                return;
            }
            List<string> pieces = Chunks.Split(json);
            for (int i = 0; i < pieces.Count; i++)
            {
                rpc.SendAsk(id, i, pieces.Count, pieces[i]);
            }
        });
    }

    /// <summary>K on the page (not while typing): chat takes the keyboard; when chat closes, it comes back (Update).</summary>
    [ReceiveFromCef]
    private void OnMesolChat(BrowserHandle page)
    {
        Guard("K", () =>
        {
            if (!_open || chat.IsOpen)
            {
                return;
            }
            cef.SetFocused(_viewport, false);
            chat.Open();
            _chatWasOpen = true;
        });
    }

    /// <summary>
    /// The page is open but lost the keyboard: a mouse click in UI-only input hands it to the game, and the card seemed
    /// to hang until End twice. Give it back, unless chat or RpRoll's board has it on purpose.
    /// </summary>
    [ReceiveFromCef]
    private void OnMesolLostFocus(BrowserHandle page)
    {
        Guard("getting the keyboard back", () =>
        {
            if (!_open || chat.IsOpen || AppDomain.CurrentDomain.GetData(BoardOpenKey) is true || Now - _refocusedAt < 0.3)
            {
                return;
            }
            _refocusedAt = Now;
            cef.SetFocused(_viewport, true);
            cef.SetPageFocused(page, true);
            cef.SetCursorVisible(false);
            if (!_refocusLogged)
            {
                _refocusLogged = true;
                logger.LogInformation("[MESOL] The card lost the keyboard (a click?) and got it back.");
            }
        });
    }

    /// <summary>The page has a text box (or a search box) with the keyboard, or not any more.</summary>
    [ReceiveFromCef]
    private void OnMesolTyping(BrowserHandle page, bool typing)
    {
        SetTyping(typing && _open);
    }

    private static void SetTyping(bool typing) => AppDomain.CurrentDomain.SetData(TextInputKey, typing);

    [ReceiveFromCef]
    private void OnMesolPanelReady(BrowserHandle page)
    {
        logger.LogInformation("[MESOL] Offer panel loaded.");
    }

    /// <summary>The panel has nothing left on screen: hide its viewport until the next message.</summary>
    [ReceiveFromCef]
    private void OnMesolPanelEmpty(BrowserHandle page)
    {
        Guard("hiding the offer panel", () =>
        {
            _panelShown = false;
            cef.SetVisible(_panelViewport, false);
        });
    }

    [ReceiveFromCef]
    private void OnMesolClose(BrowserHandle page)
    {
        Guard("Escape", () => SetOpen(false));
    }
}
