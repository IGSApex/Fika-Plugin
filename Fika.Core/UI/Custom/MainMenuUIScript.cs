using EFT.Communications;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using Fika.Core.Bundles;
using Fika.Core.Main.Utils;
using Fika.Core.Networking;
using Fika.Core.Networking.Http;
using Fika.Core.Networking.Models;
using Fika.Core.Networking.Models.Presence;
using Fika.Core.Networking.Websocket;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;
using JsonType;
using static Fika.Core.UI.FikaUIGlobals;

namespace Fika.Core.UI.Custom;

public class MainMenuUIScript : MonoBehaviour
{
    public static bool Exist
    {
        get
        {
            return _instance != null;
        }
    }

    public static MainMenuUIScript Instance
    {
        get
        {
            return _instance;
        }
    }

    public bool JoinInProgress
    {
        get => _joinInProgress;
        set
        {
            lock (_joinInProgressLock)
            {
                _joinInProgress = value;
            }
        }
    }

    private static MainMenuUIScript _instance;

    private Coroutine _queryRoutine;
    private MainMenuUI _mainMenuUI;
    private GameObject _playerTemplate;
    private GameObject _userInterface;
    private Button _squadActionButton;
    private Button _squadManageButton;
    private TextMeshProUGUI _squadActionLabel;
    private GameObject _squadManagePanel;
    private TextMeshProUGUI _squadManageLabel;
    private TextMeshProUGUI _squadIdLabel;
    private RectTransform _squadRosterRoot;
    private GameObject _pendingInviteBar;
    private TextMeshProUGUI _pendingInviteLabel;
    private readonly Dictionary<string, string> _onlineNames = new();
    private List<GameObject> _players;
    private DateTime _lastRefresh;
    private DateTime _lastSet;
    private RectTransform _transformToScale;
    private IMatchmakerSession<RaidSettings> _backendSession;

    private bool _joinInProgress;
    private readonly object _joinInProgressLock = new();

    private const int _minSecondsToWait = 2;

    private DateTime BackendTime
    {
        get
        {
            if (_backendSession == null)
            {
                return DateTime.MinValue;
            }
            return _backendSession.GetCurrentLocationTime;
        }
    }

    private DateTime StaticTime
    {
        get
        {
            return new DateTime(2016, 8, 4, 15, 28, 0);
        }
    }

    protected void Start()
    {
        _instance = this;
        _players = [];
        _lastRefresh = DateTime.Now;
        _lastSet = DateTime.Now;
        if (TarkovApplication.Exist(out var tarkovApplication) && tarkovApplication.Session != null)
        {
            _backendSession = tarkovApplication.Session;
        }
        CreateMainMenuUI();
    }

    protected void OnEnable()
    {
        if (!FikaPlugin.Instance.Settings.EnableOnlinePlayers.Value)
        {
            if (_userInterface != null)
            {
                _userInterface.SetActive(false);
            }
            return;
        }

        if (_userInterface != null)
        {
            _userInterface.SetActive(true);
        }

        if (_transformToScale != null)
        {
            var scale = FikaPlugin.Instance.Settings.OnlinePlayersScale.Value;
            _transformToScale.localScale = new(scale, scale, scale);
        }
        _queryRoutine = StartCoroutine(QueryPlayers());
    }

    protected void OnDisable()
    {
        if (_queryRoutine != null)
        {
            StopCoroutine(_queryRoutine);
        }
    }

    private void CreateMainMenuUI()
    {
        var mainMenuUIPrefab = InternalBundleLoader.Instance.GetFikaAsset(InternalBundleLoader.EFikaAsset.MainMenuUI);
        var mainMenuUI = Instantiate(mainMenuUIPrefab);
        _mainMenuUI = mainMenuUI.GetComponent<MainMenuUI>();
        _playerTemplate = _mainMenuUI.PlayerTemplate;
        var newParent = Singleton<CommonUI>.Instance.MenuScreen.gameObject.transform;
        mainMenuUI.transform.SetParent(newParent);
        gameObject.transform.SetParent(newParent);

        var mainMenuUITransform = _mainMenuUI.gameObject.transform;
        var objectToAttach = mainMenuUITransform.GetChild(0).GetChild(0).gameObject;
        _userInterface = objectToAttach;
        _transformToScale = objectToAttach.RectTransform();
        var scale = FikaPlugin.Instance.Settings.OnlinePlayersScale.Value;
        _transformToScale.localScale = new(scale, scale, scale);
        objectToAttach.AddComponent<UIDragComponent>().Init(_transformToScale, true);
        _mainMenuUI.RefreshButton.onClick.AddListener(ManualRefresh);
        CreateSquadControls();
        CreatePendingInviteBar();
        _ = RefreshSquadControls();

        gameObject.SetActive(false);
        this.WaitFrames(3, null);
        gameObject.SetActive(true);
    }


    private Button MakeSquadButton(string title, Transform parent, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(118, 27);
        go.GetComponent<Image>().color = new Color32(0x27, 0x2B, 0x2D, 0xFF);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var label = textGo.GetComponent<TextMeshProUGUI>();
        label.text = title;
        label.fontSize = 15;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        return button;
    }

    private void CreateSquadControls()
    {
        // Separate footer below the Online Players panel.
        var root = new GameObject("FikaSquadControls", typeof(RectTransform), typeof(Image));
        root.transform.SetParent(_userInterface.transform, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -6f);
        rect.sizeDelta = new Vector2(-4f, 44f);
        root.GetComponent<Image>().color = new Color32(0x58, 0x5E, 0x61, 0xFF);

        // A solid two-pixel border keeps the dark fill entirely inside.
        var background = new GameObject("SquadFooterBackground", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(root.transform, false);
        var backgroundRect = (RectTransform)background.transform;
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = new Vector2(2f, 2f);
        backgroundRect.offsetMax = new Vector2(-2f, -2f);
        background.GetComponent<Image>().color = new Color32(0x12, 0x13, 0x14, 0xFF);
        var layout = background.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(7, 7, 5, 5);
        layout.spacing = 8;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;
                _squadActionButton = MakeSquadButton("Create Squad", background.transform, async () =>
        {
            _squadActionButton.interactable = false;
            try
            {
                if (string.IsNullOrEmpty(FikaSquadNotifications.SquadId))
                    await FikaSquadNotifications.Create();
                else
                    await FikaSquadNotifications.Leave();
                await RefreshSquadControls();
            }
            catch (Exception ex) { FikaGlobals.LogError("Squad action failed: " + ex); }
            finally { _squadActionButton.interactable = true; }
        });
        _squadActionLabel = _squadActionButton.GetComponentInChildren<TextMeshProUGUI>();
        _squadManageButton = MakeSquadButton("Manage Squad", background.transform, () =>
        {
            if (!FikaSquadNotifications.IsLeader) return;
            _squadManagePanel.SetActive(!_squadManagePanel.activeSelf);
            UpdateSquadManageLabel();
        });

        _squadManagePanel = new GameObject("SquadManagePanel", typeof(RectTransform), typeof(Image));
        _squadManagePanel.transform.SetParent(_userInterface.transform, false);
        var panelRect = (RectTransform)_squadManagePanel.transform;
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0, -48);
        panelRect.sizeDelta = new Vector2(-10, 245);
        // Same dark panel surface and subtle border as the online-player list.
        _squadManagePanel.GetComponent<Image>().color = new Color(0.065f, 0.075f, 0.078f, 0.99f);
        var manageOutline = _squadManagePanel.AddComponent<Outline>();
        manageOutline.effectColor = new Color(0.36f, 0.41f, 0.42f, 1f);
        manageOutline.effectDistance = new Vector2(1f, -1f);
        // Header bar mirrors the Online Players title bar.
        var header = new GameObject("SquadMembersHeader", typeof(RectTransform), typeof(Image));
        header.transform.SetParent(_squadManagePanel.transform, false);
        var headerRect = (RectTransform)header.transform;
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1);
        headerRect.anchoredPosition = Vector2.zero;
        headerRect.sizeDelta = new Vector2(0, 36);
        header.GetComponent<Image>().color = new Color(0.14f, 0.17f, 0.18f, 0.99f);
        var labelGo = new GameObject("MembersLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(header.transform, false);
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(10, 0);
        labelRect.offsetMax = new Vector2(-10, 0);
        _squadManageLabel = labelGo.GetComponent<TextMeshProUGUI>();
        _squadManageLabel.fontSize = 17;
        _squadManageLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _squadManageLabel.color = Color.white;

        // Squad identifier is a small footer, similar to a version label.
        var footer = new GameObject("SquadIdFooter", typeof(RectTransform), typeof(Image));
        footer.transform.SetParent(_squadManagePanel.transform, false);
        var footerRect = (RectTransform)footer.transform;
        footerRect.anchorMin = new Vector2(0, 0);
        footerRect.anchorMax = new Vector2(1, 0);
        footerRect.pivot = new Vector2(0.5f, 0);
        footerRect.anchoredPosition = Vector2.zero;
        footerRect.sizeDelta = new Vector2(0, 25);
        footer.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.15f, 0.99f);
        var idGo = new GameObject("SquadIdLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        idGo.transform.SetParent(footer.transform, false);
        var idRect = (RectTransform)idGo.transform;
        idRect.anchorMin = Vector2.zero;
        idRect.anchorMax = Vector2.one;
        idRect.offsetMin = new Vector2(9, 0);
        idRect.offsetMax = new Vector2(-9, 0);
        _squadIdLabel = idGo.GetComponent<TextMeshProUGUI>();
        _squadIdLabel.fontSize = 11;
        _squadIdLabel.alignment = TextAlignmentOptions.MidlineLeft;
        _squadIdLabel.color = new Color(0.67f, 0.72f, 0.73f, 1f);
        _squadIdLabel.overflowMode = TextOverflowModes.Ellipsis;
        var roster = new GameObject("MemberRoster", typeof(RectTransform));
        roster.transform.SetParent(_squadManagePanel.transform, false);
        _squadRosterRoot = (RectTransform)roster.transform;
        _squadRosterRoot.anchorMin = new Vector2(0, 1);
        _squadRosterRoot.anchorMax = new Vector2(1, 1);
        _squadRosterRoot.pivot = new Vector2(0.5f, 1);
        _squadRosterRoot.anchoredPosition = new Vector2(0, -45);
        _squadRosterRoot.sizeDelta = new Vector2(-16, 165);
        _squadManagePanel.SetActive(false);
    }


    private void CreatePendingInviteBar()
    {
        _pendingInviteBar = new GameObject("PendingSquadInvite", typeof(RectTransform), typeof(Image));
        _pendingInviteBar.transform.SetParent(_userInterface.transform, false);
        var rect = (RectTransform)_pendingInviteBar.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0, 5);
        rect.sizeDelta = new Vector2(400, 64);
        _pendingInviteBar.GetComponent<Image>().color = new Color(0.09f, 0.12f, 0.12f, 0.98f);
        var textGo = new GameObject("InviteMessage", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(_pendingInviteBar.transform, false);
        var labelRect = (RectTransform)textGo.transform;
        labelRect.anchorMin = new Vector2(0, 0.5f);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.offsetMin = new Vector2(8, 0);
        labelRect.offsetMax = new Vector2(-8, -3);
        _pendingInviteLabel = textGo.GetComponent<TextMeshProUGUI>();
        _pendingInviteLabel.fontSize = 15;
        _pendingInviteLabel.alignment = TextAlignmentOptions.Center;

        var accept = MakeSquadButton("Accept", _pendingInviteBar.transform, () => _ = RespondToFirstInvite(true));
        var decline = MakeSquadButton("Decline", _pendingInviteBar.transform, () => _ = RespondToFirstInvite(false));
        foreach (var button in new[] { accept, decline })
        {
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = new Vector2(0.5f, 0);
            buttonRect.anchorMax = new Vector2(0.5f, 0);
            buttonRect.pivot = new Vector2(0.5f, 0);
            buttonRect.sizeDelta = new Vector2(105, 26);
        }
        ((RectTransform)accept.transform).anchoredPosition = new Vector2(-57, 4);
        ((RectTransform)decline.transform).anchoredPosition = new Vector2(57, 4);
        _pendingInviteBar.SetActive(false);
    }

    private async System.Threading.Tasks.Task RespondToFirstInvite(bool accept)
    {
        var pending = FikaSquadNotifications.PendingInvites.FirstOrDefault();
        if (string.IsNullOrEmpty(pending.Key)) return;
        try
        {
            var success = await FikaSquadNotifications.Respond(pending.Key, accept);
            if (!success)
                NotificationManager.DisplayMessageNotification("Squad invitation expired or could not be processed");
            await RefreshSquadControls();
        }
        catch (Exception ex) { FikaGlobals.LogError("Squad invitation response failed: " + ex); }
    }

    private void UpdatePendingInviteBar()
    {
        if (_pendingInviteBar == null) return;
        var pending = FikaSquadNotifications.PendingInvites.FirstOrDefault();
        var hasInvite = !string.IsNullOrEmpty(pending.Key);
        _pendingInviteBar.SetActive(hasInvite);
        if (hasInvite)
        {
            var name = _onlineNames.TryGetValue(pending.Value, out var nickname) ? nickname : pending.Value;
            _pendingInviteLabel.text = name + " invited you to a squad";
        }
    }

    private void UpdateSquadManageLabel()
    {
        if (_squadManageLabel == null || _squadRosterRoot == null) return;
        var members = FikaSquadNotifications.Members;
        _squadManageLabel.text = "Members: " + members.Length + " / 5";
        if (_squadIdLabel != null)
            _squadIdLabel.text = "Squad: " + FikaSquadNotifications.SquadId;
        foreach (Transform child in _squadRosterRoot)
            Destroy(child.gameObject);
        for (var i = 0; i < members.Length && i < 5; i++)
        {
            var memberId = members[i];
            var row = new GameObject("Member_" + i, typeof(RectTransform), typeof(Image));
            row.transform.SetParent(_squadRosterRoot, false);
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = new Vector2(0, 1);
            rowRect.anchorMax = new Vector2(1, 1);
            rowRect.pivot = new Vector2(0.5f, 1);
            rowRect.anchoredPosition = new Vector2(0, -i * 33);
            rowRect.sizeDelta = new Vector2(0, 30);
            row.GetComponent<Image>().color = new Color(0.13f, 0.15f, 0.15f, 0.95f);
            var labelGo = new GameObject("MemberName", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(row.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8, 0);
            labelRect.offsetMax = new Vector2(-82, 0);
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.fontSize = 14;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var isSelf = memberId == FikaBackendUtils.Profile?.Id;
            label.text = (_onlineNames.TryGetValue(memberId, out var nickname) ? nickname : memberId)
                + (isSelf ? " (Leader)" : "");
            if (!isSelf && FikaSquadNotifications.IsLeader)
            {
                var kick = MakeSquadButton("Kick", row.transform, async () =>
                {
                    try
                    {
                        if (!await FikaSquadNotifications.Kick(memberId))
                            NotificationManager.DisplayMessageNotification("Unable to kick squad member");
                        UpdateSquadManageLabel();
                    }
                    catch (Exception ex) { FikaGlobals.LogError("Squad kick failed: " + ex); }
                });
                var kickRect = (RectTransform)kick.transform;
                kickRect.anchorMin = new Vector2(1, 0.5f);
                kickRect.anchorMax = new Vector2(1, 0.5f);
                kickRect.pivot = new Vector2(1, 0.5f);
                kickRect.anchoredPosition = new Vector2(-5, 0);
                kickRect.sizeDelta = new Vector2(68, 25);
            }
        }
    }

    private async System.Threading.Tasks.Task RefreshSquadControls()
    {
        try
        {
            await FikaSquadNotifications.Refresh();
            if (_squadActionLabel == null) return;
            var inSquad = !string.IsNullOrEmpty(FikaSquadNotifications.SquadId);
            _squadActionLabel.text = inSquad ? "Leave Squad" : "Create Squad";
            _squadManageButton.gameObject.SetActive(inSquad && FikaSquadNotifications.IsLeader);
            if (!inSquad || !FikaSquadNotifications.IsLeader)
                _squadManagePanel.SetActive(false);
            UpdateSquadManageLabel();
            UpdatePendingInviteBar();
        }
        catch (Exception ex) { FikaGlobals.LogError("Squad refresh failed: " + ex); }
    }

    private void ManualRefresh()
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds >= 5)
        {
            _lastRefresh = DateTime.Now;
            ClearAndQueryPlayers();
        }
    }

    private IEnumerator QueryPlayers()
    {
        WaitForEndOfFrame waitForEndOfFrame = new();
        WaitForSeconds waitForSeconds = new(10);
        while (true)
        {
            yield return waitForEndOfFrame;
            ClearAndQueryPlayers();
            yield return waitForSeconds;
        }
    }

    private void ClearAndQueryPlayers()
    {
        foreach (var item in _players)
        {
            Destroy(item);
        }
        _players.Clear();

        var response = FikaRequestHandler.GetPlayerPresences();
        _mainMenuUI.UpdateLabel(response.Length);
        _onlineNames.Clear();
        foreach (var presence in response)
            if (!string.IsNullOrEmpty(presence.ProfileId))
                _onlineNames[presence.ProfileId] = presence.Nickname;
        SetupPlayers(ref response);
        _ = RefreshSquadControls();
    }

    private string ConvertToTime(EDateTime dateTime, bool staticTime)
    {
        if (staticTime)
        {
            var staticDate = StaticTime;
            return dateTime == EDateTime.CURR ? staticDate.ToString("HH:mm:ss") : staticDate.AddHours(-12).ToString("HH:mm:ss");
        }

        var backendTime = BackendTime;
        if (backendTime == DateTime.MinValue)
        {
            return "ERROR";
        }
        return dateTime == EDateTime.CURR ? backendTime.ToString("HH:mm:ss") : backendTime.AddHours(-12).ToString("HH:mm:ss");
    }

    private bool IsStaticTimeLocation(string location)
    {
        return location is "factory4_day" or "factory4_night" or "laboratory";
    }

    private void SetupPlayers(ref FikaPlayerPresence[] responses)
    {
        foreach (var presence in responses)
        {
            var newPlayer = Instantiate(_playerTemplate, _playerTemplate.transform.parent);
            var mainMenuUIPlayer = newPlayer.GetComponent<MainMenuUIPlayer>();
            mainMenuUIPlayer.SetActivity(presence.Nickname, presence.Level, presence.Activity);
            if (!string.IsNullOrEmpty(presence.SquadId))
            {
                var marker = new GameObject("SquadIcon", typeof(RectTransform), typeof(TextMeshProUGUI));
                marker.transform.SetParent(newPlayer.transform, false);
                var markerRect = (RectTransform)marker.transform;
                markerRect.anchorMin = new Vector2(0f, 0.5f);
                markerRect.anchorMax = new Vector2(0f, 0.5f);
                markerRect.anchoredPosition = new Vector2(245, 0);
                markerRect.sizeDelta = new Vector2(28, 24);
                var markerLabel = marker.GetComponent<TextMeshProUGUI>();
                markerLabel.text = "◆";
                markerLabel.color = new Color(0.4f, 0.85f, 0.5f);
                markerLabel.fontSize = 19;
                markerLabel.alignment = TextAlignmentOptions.Center;
                var markerTooltip = marker.AddComponent<HoverTooltipArea>();
                markerTooltip.enabled = true;
                markerTooltip.SetMessageText("Squad: " + presence.SquadId);
            }

            if (!string.IsNullOrEmpty(presence.ProfileId)
                && presence.ProfileId != FikaBackendUtils.Profile?.Id
                && presence.Activity == EFikaPlayerPresence.IN_MENU)
            {
                var invite = MakeSquadButton("Invite", _userInterface.transform, async () =>
                {
                    if (!FikaSquadNotifications.IsLeader) return;
                    try
                    {
                        var success = await FikaSquadNotifications.Invite(presence.ProfileId);
                        NotificationManager.DisplayMessageNotification(success ? "Squad invitation sent" : "Unable to invite player");
                    }
                    catch (Exception ex) { FikaGlobals.LogError("Squad invite failed: " + ex); }
                });
                var inviteRect = (RectTransform)invite.transform;
                // Position the button outside the panel, aligned with this player's row.
                // Keep it under the panel root so the row's mask does not clip it.
                var rowRect = (RectTransform)newPlayer.transform;
                var rowCorners = new Vector3[4];
                rowRect.GetWorldCorners(rowCorners);
                var panelRect = (RectTransform)_userInterface.transform;
                var rightEdge = panelRect.InverseTransformPoint(rowCorners[2]);
                var rowCenter = panelRect.InverseTransformPoint((rowCorners[0] + rowCorners[2]) * 0.5f);
                inviteRect.anchorMin = new Vector2(0.5f, 0.5f);
                inviteRect.anchorMax = new Vector2(0.5f, 0.5f);
                inviteRect.pivot = new Vector2(0f, 0.5f);
                inviteRect.anchoredPosition = new Vector2(rightEdge.x + 8f, rowCenter.y);
                inviteRect.sizeDelta = new Vector2(78, 28);
                invite.transform.SetAsLastSibling();
                invite.gameObject.SetActive(FikaSquadNotifications.IsLeader
                    && string.IsNullOrEmpty(presence.SquadId));
            }
            if (presence.Activity is EFikaPlayerPresence.IN_RAID && presence.RaidInformation.HasValue)
            {
                var information = presence.RaidInformation.Value;
#if DEBUG
                FikaGlobals.LogInfo($"Got presense: {information.Started}, {information.MatchId}");
#endif
                var side = information.Side == ESideType.Pmc ? "RaidSidePmc".Localized() : "RaidSideScav".Localized();
                var time = ConvertToTime(information.Time, IsStaticTimeLocation(information.Location));
                var tooltip = newPlayer.AddComponent<HoverTooltipArea>();
                tooltip.enabled = true;
                tooltip.SetMessageText(string.Format(LocaleUtils.UI_MMUI_RAID_DETAILS.Localized(), side,
                    ColorizeText(EColor.BLUE, information.Location.Localized()),
                    BoldText(time)));

                if (!information.Started)
                {
                    var buttonGo = mainMenuUIPlayer.JoinButton.transform.parent.gameObject;
                    buttonGo.SetActive(true);
                    var tooltip2 = buttonGo.AddComponent<HoverTooltipArea>();
                    tooltip2.enabled = true;
                    tooltip2.SetMessageText(LocaleUtils.UI_JOIN_RAID.Localized());
                    mainMenuUIPlayer.JoinButton.onClick.AddListener(async () =>
                    {
                        if (JoinInProgress)
                        {
                            return;
                        }

                        JoinInProgress = true;

                        if (!TarkovApplication.Exist(out var tarkovApplication))
                        {
                            FikaGlobals.LogError("OnFikaStartRaid: Could not find TarkovApplication");
                            JoinInProgress = false;
                            return;
                        }

                        var session = tarkovApplication.Session;
                        if (session == null)
                        {
                            Singleton<PreloaderUI>.Instance.ShowErrorScreen("ERROR JOINING",
                                "Session was null when starting the raid");
                            FikaGlobals.LogError("Session was null when starting the raid");
                            JoinInProgress = false;
                            return;
                        }

                        FikaBackendUtils.Profile = session.Profile;

                        var location = session.LocationSettings.locations.Values
                            .FirstOrDefault(l => l.Id == information.Location);

                        if (location == null)
                        {
                            Singleton<PreloaderUI>.Instance.ShowErrorScreen("ERROR JOINING",
                                $"Failed to find location {information.Location}");
                            FikaGlobals.LogError($"Failed to find location {information.Location}");
                            JoinInProgress = false;
                            return;
                        }

                        var mmc = tarkovApplication._menuOperation;

#if !DEBUG
                        if (location.AccessKeys.Length > 0 && information.Side is ESideType.Pmc)
                        {
                            var playerLevel = session.Profile.Info.Level;
                            if (playerLevel >= location.MinPlayerLvlAccessKeys)
                            {
                                var playerItems = mmc.InventoryController.Inventory.GetPlayerItems(EPlayerItems.Equipment);
                                var item = playerItems.FirstOrDefault(i => mmc.InventoryController.Examined(i) && location.AccessKeys.Contains(i.StringTemplateId));
                                if (item == null)
                                {
                                    NotificationManager.DisplayMessageNotification(string.Format(LocaleUtils.MISSING_KEY_FOR_LOCATION.Localized(),
                                        $"{location.AccessKeys[0]} Name".Localized()),
                                        iconType: ENotificationIconType.Alert);
                                    JoinInProgress = false;
                                    return;
                                }
                            }
                        }
#endif

                        NotificationManager.DisplayMessageNotification(LocaleUtils.CONNECTING_TO_SESSION.Localized(),
                            iconType: ENotificationIconType.EntryPoint);
                        using var pingingClient = await NetManagerUtils.CreatePingingClient();

                        if (pingingClient.Init(information.MatchId))
                        {
                            FikaGlobals.LogInfo("Attempting to connect to host session...");
                            var knockMessage = FikaBackendUtils.ServerGuid.ToString();
                            var success = await pingingClient.AttemptToPingHost(knockMessage, false);

                            if (!success)
                            {
                                Singleton<PreloaderUI>.Instance.ShowCriticalErrorScreen(
                                LocaleUtils.UI_ERROR_CONNECTING.Localized(),
                                LocaleUtils.UI_UNABLE_TO_CONNECT.Localized(),
                                ErrorScreen.EButtonType.OkButton, 10f);
                                JoinInProgress = false;
                                return;
                            }
                        }
                        else
                        {
                            Singleton<PreloaderUI>.Instance.ShowCriticalErrorScreen(
                                LocaleUtils.UI_ERROR_CONNECTING.Localized(),
                                LocaleUtils.UI_PINGER_START_FAIL.Localized(),
                                ErrorScreen.EButtonType.OkButton, 10f);
                            JoinInProgress = false;
                            return;
                        }

                        if (FikaBackendUtils.JoinMatch(FikaBackendUtils.Profile.Id, information.MatchId, out var result, out var errorMessage))
                        {
                            FikaBackendUtils.GroupId = result.ServerId;
                            FikaBackendUtils.ClientType = EClientType.Client;

                            AddPlayerRequest data = new(FikaBackendUtils.GroupId, FikaBackendUtils.Profile.Id, FikaBackendUtils.IsSpectator);
                            FikaRequestHandler.UpdateAddPlayer(data);

                            RaidSettings raidSettings = new()
                            {
                                Side = information.Side,
                                PlayersSpawnPlace = default,
                                MetabolismDisabled = default,
                                BotSettings = default,
                                WavesSettings = default,
                                TimeAndWeatherSettings = default,
                                SelectedDateTime = information.Time,
                                SelectedLocation = location,
                                isInTransition = false,
                                RaidMode = ERaidMode.Local,
                                IsPveOffline = true,
                                OnlinePveRaid = false
                            };

                            FikaBackendUtils.IsScav = raidSettings.IsScav;

                            var controller = mmc.MatchmakerPlayersController;

                            if (raidSettings.Side is ESideType.Savage && ScavBlocked(session, out var minutesLeft))
                            {
                                Singleton<PreloaderUI>.Instance.ShowErrorScreen("ERROR JOINING",
                                    string.Format(LocaleUtils.UI_SCAV_NOT_READY.Localized(), minutesLeft.ToString("F0")));
                                JoinInProgress = false;
                                return;
                            }

                            tarkovApplication._raidSettings = raidSettings;
                            controller.MatchingStartTime = DateTimeExtensions.Now;

                            try
                            {
                                await tarkovApplication.LocalGameMatching(raidSettings.TimeAndWeatherSettings);
                            }
                            catch (Exception ex)
                            {
                                Singleton<PreloaderUI>.Instance.ShowErrorScreen("ERROR JOINING",
                                    $"Exception caught during raid init: {ex.Message}", Application.Quit);
                                JoinInProgress = false;
                            }
                        }
                        else
                        {
                            Singleton<PreloaderUI>.Instance.ShowErrorScreen("ERROR JOINING", errorMessage, null);
                        }

                        JoinInProgress = false;
                    });
                }
            }
            newPlayer.SetActive(true);
            _players.Add(newPlayer);
        }
    }

    private bool ScavBlocked(IEftSession session, out double remainingMinutes)
    {
        var utcNowUnixInt = DateTimeExtensions.UtcNowUnixInt;
        var diffSeconds = session.ProfileOfPet.Info.SavageLockTime - (double)utcNowUnixInt;

        if (diffSeconds > 0.0d)
        {
            remainingMinutes = diffSeconds / 60.0d;
            return true;
        }

        remainingMinutes = 0.0d;
        return false;
    }

    public void UpdatePresence(EFikaPlayerPresence presence)
    {
        // Prevent spamming going back and forth to the main menu causing server lag for no reason
        if ((DateTime.Now - _lastSet).TotalSeconds < _minSecondsToWait)
        {
            return;
        }

        _lastSet = DateTime.Now;
        FikaSetPresence data = new(presence);
        FikaRequestHandler.SetPresence(data);
    }
}
