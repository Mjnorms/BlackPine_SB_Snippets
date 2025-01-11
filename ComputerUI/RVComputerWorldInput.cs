//////////////////////////////////////////////////////////////////////////////
/// Handles all things RV Computer UI

using CustomInspectorAttributes.Rewrite.Character;
using FMODUnity;
using Interactions;
using Items;
using PLI;
using PLI.AI;
using PLI.CharacterStats;
using PLI.Extensions;
using PLI.GameCore;
using PLI.UIElements;
using PLI.Lobbies;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using PLI.Audio;
using PLI.Net;
using UnityEngine.Video;
using FMOD.Studio;
using UnityEditor;
using System.Runtime.InteropServices;
using PLI.Rendering;
using PLI.Unity_Services;
using PLI.ECS.Game;

#region Helper Classes
[System.Serializable]
public class ContractContents
{
    public string subject = "";
    public string body = "";
    [HideInInspector]
    public bool isRead = true;
    [HideInInspector]
    public bool isAccepted = false;
}

public enum EmailType { Standard, Video, Photo, GIF }
[System.Serializable]
public class EMailContents
{
    public EmailType emailType;
    public string subject = "";
    public string body = "";
    [HideInInspector]
    public bool isRead = false;

    // Video email-specific fields
    public VideoClip videoClip;
    public string videoTranscript;

    // Gif email-specific fields
    public VideoClip gifVideoClip;

    // Photo email-specific fields
    public Texture2D photoTexture;
    public string photoDescriptionHeader;
    public string photoDescription;
}
#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(EMailContents))]
public class EmailContentsDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        position.height = EditorGUIUtility.singleLineHeight;

        // Draw emailType selection
        var emailTypeProp = property.FindPropertyRelative("emailType");
        EditorGUI.PropertyField(position, emailTypeProp);
        position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        // Draw standard fields
        EditorGUI.PropertyField(position, property.FindPropertyRelative("subject"));
        position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(position, property.FindPropertyRelative("body"));
        position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

        // Draw type-specific fields
        switch ((EmailType)emailTypeProp.enumValueIndex)
        {
            case EmailType.Video:
                EditorGUI.PropertyField(position, property.FindPropertyRelative("videoClip"));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("videoTranscript"));
                break;

            case EmailType.GIF:
                EditorGUI.PropertyField(position, property.FindPropertyRelative("gifVideoClip"));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("photoDescriptionHeader"));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("photoDescription"));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                break;

            case EmailType.Photo:
                EditorGUI.PropertyField(position, property.FindPropertyRelative("photoTexture"));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("photoDescriptionHeader"));
                position.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.PropertyField(position, property.FindPropertyRelative("photoDescription"));
                break;
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight * 4 + EditorGUIUtility.standardVerticalSpacing * 3;

        // Adjust height for additional fields
        var emailType = (EmailType)property.FindPropertyRelative("emailType").enumValueIndex;
        if (emailType == EmailType.Video || emailType == EmailType.Photo)
        {
            height += EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
        }

        return height;
    }
}
#endif

[System.Serializable]
public class FMODEvents
{
    public string eventName;
    public string eventPath;
}
class ComputerItemData
{
    public ItemDisplayData displayData;
    public string GUID;
    public uint price;
}

public static class CurrencyManager
{
    // Load currency
    public static int LoadCurrency()
    {
        if (Local.Player.ControlledCharacter.TryGetComponent(out CharacterStatus status))
        {
            if (status && status.HasStat(Stat.Currency))
            {
                CharacterStat currency = status.GetStat(Stat.Currency);
                int currentCurrency = (int)currency.Current.Value;
                return currentCurrency;
            }
            else
            {
                Debug.LogError("[CurrencyManager] Currency stat not found.");
            }
        }
        else
        {
            Debug.LogError("[CurrencyManager] Player does not have a CharacterStatus component.");
        }

        return -1;
    }

    // Adjust currency
    public static bool _AdjustCurrencyForPlayer(string authID, int amount)
    {
        if (!NetMgr.IsServer) { return false; }
            
        if (string.IsNullOrEmpty(authID)) { return false; }

        // Grab auth id -> player, bail if no character
        var player = PlayerNetworkData.GetActivePlayerByAuthID(authID);
        if (player == null) { return false; }
        if (player.ControlledCharacter == null) { return false; }

        // Grab status comp. If not found or no stat, bail
        CharacterStatus status;
        bool hasStatusComp = (player.ControlledCharacter.TryGetComponent(out status));
        if (!hasStatusComp)
        {
            Debug.LogError("[CurrencyManager] Player does not have a CharacterStatus component.");
            return false;
        }

        if (!status || !status.HasStat(Stat.Currency))
        {
            Debug.LogError("[CurrencyManager] Currency stat not found.");
            return false;
        }

        // update
        CharacterStat currency = status.GetStat(Stat.Currency);

        if (amount < 0) 
        {
            if (currency.Current.Value < -amount)
            {
                Debug.LogError("[CurrencyManager] Not enough currency to complete the transaction.");
                return false;
            }
        }

        CharacterStatus.Adjust(ref currency.Current, amount);
        status.SetStat(Stat.Currency, currency);

        PLI.Logging.Core.Channels.GameCore.Info($"Added {amount} to {authID}");
        return true;
    }


    // Allowed to spend currency
    // Eh, Don't love this. Checks on the server side, but want to know if we can req transfer w/o an additional rpc
    // TODO : send buy/sell w/ item info to server instead and handle everything there. #TIMEBOMB#
    public static bool ClientAllowedToSpendCurrency(int amount)
    {
        if (Local.Player.ControlledCharacter.TryGetComponent(out CharacterStatus status))
        {
            if (status && status.HasStat(Stat.Currency))
            {
                CharacterStat currency = status.GetStat(Stat.Currency);

                if (currency.Current.Value >= amount)
                {
                    CharacterStatus.Adjust(ref currency.Current, -amount);
                    status.SetStat(Stat.Currency, currency);

                    return true;
                }
                else
                {
                    Debug.Log("[CurrencyManager] Not enough currency to complete the transaction.");
                }
            }
            else
            {
                Debug.LogError("[CurrencyManager] Currency stat not found.");
            }
        }
        else
        {
            Debug.LogError("[CurrencyManager] Player does not have a CharacterStatus component.");
        }

        return false;
    }
}
#endregion

public class RVComputerWorldInput : NetworkBehaviour
{
    #region Class Variables
    private UIDocument document;
    private Camera playerCam;

    public EMailContents[] emails;
    private int maxEmails = 10;

    public ContractContents[] contracts;
    private int maxContracts = 10;

    private int maxCameras = 9;

    Color evenColor = new Color(40 / 255f, 26 / 255f, 1 / 255f); // RGB(40, 26, 1)
    Color oddColor = new Color(50 / 255f, 33 / 255f, 1 / 255f);  // RGB(50, 33, 1)

    private Dictionary<string, FMOD.Studio.EventInstance> fmodEvents;
    public FMODEvents[] sfxEvents;
    public EventReference mitchAudio;
    private EventInstance mitchAudioInstance;

    private FSM<Tab> FSM;
    private NetworkVariable<Tab> activeTab = new NetworkVariable<Tab>(Tab.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Button EmailButton;
    private Button ContractsButton;
    private Button MapButton;
    private Button SupportButton;
    private Button RVButton;
    private Button StoreButton;
    private Button InventoryButton;
    List<VisualElement> ContentPages = new List<VisualElement>();

    private VisualElement Tabs;
    private VisualElement BaseMenu;
    private VisualElement Emails;
    private VisualElement EmailContent;
    private ScrollView EmailsList;
    private VisualElement Contracts;
    private VisualElement ContractContentElement;
    private ScrollView ContractsList;
    private VisualElement MapContent;
    private VisualElement MapView;
    private VisualElement MapPlayerStatusContainer;
    private VisualElement Support;
    private VisualElement SupportContent;
    private ScrollView CameraScrollView;
    private VisualElement CameraFrame;
    private VisualElement RVContent;
    private VisualElement RVHealthBar;
    private Label RVHealthPercentageLabel;
    private VisualElement StoreContent;
    private ScrollView StoreScrollView;
    private ScrollView CartScrollView;
    private InventoryAuthoring BuyFrom_StoreContainer;
    private Button PurchaseButton;
    private VisualElement InventoryContent;
    private ScrollView InventoryScrollView;
    private ScrollView InvenCartScrollView;
    private Button SellButton;
    private InventoryAuthoring SellTo_StoreContainer;

    [Header("NO TOUCHY, required references")]
    [SerializeField]
    private VisualTreeAsset mailContent;
    [SerializeField]
    private VisualTreeAsset mailContent_Video;
    [SerializeField]
    private VisualTreeAsset mailContent_Photo;
    [SerializeField]
    private VisualTreeAsset mailContent_GIF;
    [SerializeField]
    private VisualTreeAsset contractContent;
    [SerializeField]
    private VisualTreeAsset scrollViewItem;
    [SerializeField]
    private VisualTreeAsset storeItemButton;
    [SerializeField]
    private Camera mapCam;
    [SerializeField]
    private GameObject screen;
    [SerializeField]
    private VideoPlayer videoPlayer;
    [SerializeField]
    private GameObject interactionBlocker;
    #endregion

    private void Awake()
    {
        #region Screen On/Off
        FocusObject focusObjectHandler = GetComponent<FocusObject>();
        if (focusObjectHandler != null)
        {
            focusObjectHandler.OnFocus += DoOnFocus;
            focusObjectHandler.OnUnFocus += DoOnUnFocus;
        }
        #endregion

    }
    private void Start()
    {
        document = GetComponent<UIDocument>();
        Setup();
        SetupSFX();

    }
    private void Setup()
    {
        #region Tab Setup
        Tabs = document.rootVisualElement.Q("Tabs");
        EmailButton = document.rootVisualElement.Q<Button>("Email");
        EmailButton.clicked += ClickEmail;
        ContractsButton = document.rootVisualElement.Q<Button>("Contracts");
        ContractsButton.clicked += ClickContracts;
        MapButton = document.rootVisualElement.Q<Button>("Map");
        MapButton.clicked += ClickMap;
        SupportButton = document.rootVisualElement.Q<Button>("Support");
        SupportButton.clicked += ClickSupport;
        RVButton = document.rootVisualElement.Q<Button>("RV");
        RVButton.clicked += ClickRV;
        StoreButton = document.rootVisualElement.Q<Button>("Store");
        StoreButton.clicked += ClickStore;
        InventoryButton = document.rootVisualElement.Q<Button>("Inventory");
        InventoryButton.clicked += ClickInventory;

        FSM = new(this,
        new(Tab.None, Init, null, HideLogo),
        new(Tab.Email, MailEnter, null, MailExit),
        new(Tab.Contracts, ContractsEnter, null, ContractsExit),
        new(Tab.Map, MapEnter, MapUpdate, MapExit),
        new(Tab.Support, SupportEnter, null, SupportExit),
        new(Tab.RV, RVEnter, RVUpdate, RVExit),
        new(Tab.Store, StoreEnter, StoreUpdate, StoreExit),
        new(Tab.Inventory, InventoryEnter, InventoryUpdate, InventoryExit));

        activeTab.OnValueChanged += OnTabChanged;
        #endregion

        #region Element References

        BaseMenu = document.rootVisualElement.Q<VisualElement>("RVMenu");

        Emails = document.rootVisualElement.Q<VisualElement>("EmailsList");
        ContentPages.Add(Emails);
        EmailContent = Emails.Q<VisualElement>("Content");
        EmailsList = document.rootVisualElement.Q<ScrollView>("MailList");

        Contracts = document.rootVisualElement.Q<VisualElement>("ContractsList");
        ContentPages.Add(Contracts);
        ContractContentElement = Contracts.Q<VisualElement>("Content");
        ContractsList = Contracts.Q<ScrollView>("ContractsScrollView");

        MapContent = document.rootVisualElement.Q<VisualElement>("MapContent");
        ContentPages.Add(MapContent);
        MapView = MapContent.Q<VisualElement>("MapView");
        MapPlayerStatusContainer = MapContent.Q<VisualElement>("PlayerStatus");

        Support = document.rootVisualElement.Q<VisualElement>("SupportContent");
        ContentPages.Add(Support);
        SupportContent = Support.Q<VisualElement>("Content");
        CameraScrollView = Support.Q<ScrollView>("CameraScrollView");
        CameraFrame = Support.Q<VisualElement>("CameraFrame");

        RVContent = document.rootVisualElement.Q<VisualElement>("RVContent");
        ContentPages.Add(RVContent);
        RVHealthBar = document.rootVisualElement.Q<VisualElement>("RVHealthBar");
        RVHealthPercentageLabel = document.rootVisualElement.Q<Label>("RVHealthPercentageLabel");

        StoreContent = document.rootVisualElement.Q<VisualElement>("StoreContent");
        ContentPages.Add(StoreContent);
        StoreScrollView = StoreContent.Q<ScrollView>("StoreView");
        StoreScrollView.userData = "StoreScrollView";
        CartScrollView = StoreContent.Q<ScrollView>("Cart");
        CartScrollView.userData = "CartScrollView";
        BuyFrom_StoreContainer = GameObject.Find("BuyFrom_StoreContainer").GetComponent<InventoryAuthoring>();
        PurchaseButton = StoreContent.Q<Button>("Purchase");
        PurchaseButton.clicked += PurchaseItemsInCart;

        InventoryContent = document.rootVisualElement.Q<VisualElement>("InventoryContent");
        ContentPages.Add(InventoryContent);
        InventoryScrollView = InventoryContent.Q<ScrollView>("InventoryView");
        InvenCartScrollView = InventoryContent.Q<ScrollView>("InvenCart");
        SellButton = InventoryContent.Q<Button>("Sell");
        SellButton.clicked += SellItemsInCart;
        SellTo_StoreContainer = GameObject.Find("SellTo_StoreContainer").GetComponent<InventoryAuthoring>();

        #endregion

        #region World Space UI Function
        document.panelSettings.SetScreenToPanelSpaceFunction((Vector2 screenPosition) =>
        {
            var invalidPosition = new Vector2(float.NaN, float.NaN);
            if (!screen)             return invalidPosition;
            if(Local.Player == null) return invalidPosition;

            if (Local.Player.PlayerBrain && !Local.Player.PlayerBrain.IsFocused) return invalidPosition;

            var camController = Local.Player.PlayerCameraController;
            if (camController == null || camController.gameObject == null) return invalidPosition;

            playerCam = camController.gameObject.GetComponent<Camera>();
            if (playerCam == null) return invalidPosition;

            var collider = screen.GetComponent<Collider>();

            var cameraRay = playerCam.ScreenPointToRay(Input.mousePosition);

            RaycastHit hit;
            if (!collider.Raycast(cameraRay, out hit, 10f))
            {
                //Debug.Log("Reaching");
                return invalidPosition;
            }
            Vector2 pixelUV = hit.textureCoord;
            pixelUV.y = 1 - pixelUV.y;
            pixelUV.x *= this.document.panelSettings.targetTexture.width;
            pixelUV.y *= this.document.panelSettings.targetTexture.height;
            return pixelUV;
        });
        #endregion

    }

    public override void OnDestroy()
    {
        // Button Callbacks
        EmailButton.clicked -= ClickEmail;
        ContractsButton.clicked -= ClickContracts;
        MapButton.clicked -= ClickMap;
        SupportButton.clicked -= ClickSupport;
        RVButton.clicked -= ClickRV;
        StoreButton.clicked -= ClickStore;

        activeTab.OnValueChanged -= OnTabChanged;

        //SFX
        if (fmodEvents != null) // may be null in parrelsync clones (fmod events ignored)
        {
            foreach (var eventInstance in fmodEvents.Values)
            {
                eventInstance.release();
            }
        }

        // Cleanup the map renderTexture
        if (renderTexture != null)
        {
            renderTexture.Release();
            renderTexture = null;
        }

        // unsub from focus events
        FocusObject focusObjectHandler = GetComponent<FocusObject>();
        if (focusObjectHandler != null)
        {
            focusObjectHandler.OnFocus -= DoOnFocus;
            focusObjectHandler.OnUnFocus -= DoOnUnFocus;
        }

        base.OnDestroy();
    }

    #region NONE Tab
    public void Init()
    {
        // mail 'new' check
        bool anyUnread = false;
        for (int i = 0; i < emails.Length; i++)
        {
            if (!emails[i].isRead) anyUnread = true;
        }
        var mailNew = document.rootVisualElement.Q("Email").Q("New");
        mailNew.visible = anyUnread;

        anyUnread = false;
        for (int i = 0; i < contracts.Length; i++)
        {
            if (!contracts[i].isRead) anyUnread = true;
        }
        var contractNew = document.rootVisualElement.Q("Contracts").Q("New");
        contractNew.visible = anyUnread;

        if (!AlertIsVisible())
            ShowLogo();
    }

    private void ShowLogo()
    {
        var logo = document.rootVisualElement.Q("LogoLarge");
        if (logo != null && logo.visible == false) logo.visible = true;
    }

    private void HideLogo()
    {
        var logo = document.rootVisualElement.Q("LogoLarge");
        if (logo != null) logo.visible = false;
    }

    private void HideAlert()
    {
        var alert = document.rootVisualElement.Q("IncomingAlert");
        if (alert != null) alert.visible = false;
    }
    private bool AlertIsVisible()
    {
        var alert = document.rootVisualElement.Q("IncomingAlert");
        if (alert != null) return alert.visible;
        return false;
    }
    #endregion

    #region Mail Tab
    private void MailEnter()
    {
        ResetMailList();
        if (!mitchAudioInstance.isValid())
        {
            mitchAudioInstance = RuntimeManager.CreateInstance(mitchAudio);
            mitchAudioInstance.setCallback(OnEventCallback, EVENT_CALLBACK_TYPE.TIMELINE_MARKER);
            FMOD.ATTRIBUTES_3D attributes = new FMOD.ATTRIBUTES_3D();
            attributes = FMODUnity.RuntimeUtils.To3DAttributes(transform);
            mitchAudioInstance.set3DAttributes(attributes);
        }
        TabEnter(Emails, EmailButton);
    }

    private void ResetMailList()
    {
        if (EmailsList == null) return;
        if (!EmailsList.visible)
        {
            EmailsList.visible = true;
            EmailsList.style.display = DisplayStyle.Flex;
        }

        EmailsList.Clear();

        for (int i = 0; i < emails.Length; i++)
        {
            var newMailItem = scrollViewItem.CloneTree();
            if (newMailItem != null)
            {
                var listItemSlot = newMailItem.Q<VisualElement>("ListItemSlot");
                listItemSlot.style.backgroundColor = i % 2 == 0 ? evenColor : oddColor;
                var newMailItemButton = newMailItem.Q<Button>();
                if (!emails[i].isRead)
                {
                    var newLabel = listItemSlot.Q<VisualElement>("ListItemLeft");
                    newLabel.style.visibility = Visibility.Visible;
                }
                var subjectLabel = listItemSlot.Q<VisualElement>("ListItemCenter").Q<Label>();
                subjectLabel.text = emails[i].subject;
                if (emails[i].emailType == EmailType.Video)
                {
                    var attachment = listItemSlot.Q<VisualElement>("Attachment");
                    attachment.visible = true;
                }
                newMailItemButton.userData = "Email" + i;
                newMailItemButton.clickable.clickedWithEventInfo += OnMailItemClicked;
                EmailsList.Add(newMailItem);
            }
        }
        for (int i = emails.Length; i < maxEmails; i++)
        {
            var newMailItem = scrollViewItem.CloneTree();
            if (newMailItem != null)
            {
                var listItemSlot = newMailItem.Q<VisualElement>("ListItemSlot");
                listItemSlot.style.backgroundColor = i % 2 == 0 ? evenColor : oddColor;
                var newMailItemButton = newMailItem.Q<Button>();
                listItemSlot.Remove(newMailItemButton);
                EmailsList.Add(newMailItem);
            }
        }
    }

    private void MailExit()
    {
        TabExit(Emails, EmailButton);
        if (mitchAudioInstance.isValid())
        {
            mitchAudioInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            mitchAudioInstance.release();
        }
        ExitMailItemRPC();
    }

    //On click a mail from list of mails
    private void OnMailItemClicked(EventBase obj)
    {
        var button = obj?.target as Button;
        string ID = button.userData as string;
        OnMailItemClickedRPC(ID);
    }

    [Rpc(SendTo.Everyone)]
    private void OnMailItemClickedRPC(string ID)
    {
        RPC.Log(this);

        if (string.IsNullOrEmpty(ID))
        {
            Debug.LogError("button userdata is wrong");
            return;
        }
        if (EmailsList == null || mailContent == null || emails == null || EmailContent == null)
        {
            Debug.LogError("EmailsList, mailContent, emails, or EmailContent is null.");
            return;
        }

        EmailsList.visible = false;
        EmailsList.style.display = DisplayStyle.None;

        for (int i = 0; i < EmailsList.childCount; i++)
        {
            if (EmailsList?.ElementAt(i)?.Q<Button>()?.userData as string == ID)
            {
                TemplateContainer emailContent;
                if (emails[i].emailType == EmailType.Video)
                {
                    emailContent = mailContent_Video.CloneTree();
                    var videoScreen = emailContent.Q<VisualElement>("VideoPlayer");
                    videoPlayer.clip = emails[i].videoClip;
                    videoScreen.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(videoPlayer.targetTexture));
                    var playButton = emailContent.Q<Button>("VideoPlayButton");
                    playButton.clicked += VideoPlayRPC;
                    var pauseButton = emailContent.Q<Button>("VideoPauseButton");
                    pauseButton.clicked += VideoPauseRPC;
                    var restartButton = emailContent.Q<Button>("VideoRestartButton");
                    restartButton.clicked += VideoRestartRPC;

                    var transcriptLabel = emailContent.Q<Label>("TranscriptLabel");
                    transcriptLabel.text = emails[i].videoTranscript;
                    var transcriptButton = emailContent.Q<Button>("TranscriptButton");
                    transcriptButton.clicked += ToggleTranscriptRPC;
                }
                else if (emails[i].emailType == EmailType.Photo)
                {
                    emailContent = mailContent_Photo.CloneTree();
                    var photo = emailContent.Q<VisualElement>("Photo");
                    if (emails[i].photoTexture != null)
                        photo.style.backgroundImage = new StyleBackground(Background.FromTexture2D(emails[i].photoTexture));
                    var photoDescHeader = emailContent.Q<Label>("DescriptionTitle");
                    photoDescHeader.text = emails[i].photoDescriptionHeader;
                    var photoDesc = emailContent.Q<Label>("DescriptionTextLabel");
                    photoDesc.text = emails[i].photoDescription;
                }
                else if (emails[i].emailType == EmailType.GIF)
                {
                    emailContent = mailContent_GIF.CloneTree();
                    var videoScreen = emailContent.Q<VisualElement>("VideoPlayer");
                    if (videoPlayer != null && videoScreen != null)
                    {
                        videoPlayer.clip = emails[i].gifVideoClip;
                        videoPlayer.Play();
                        videoScreen.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(videoPlayer.targetTexture));
                    }
                    var photoDescHeader = emailContent.Q<Label>("DescriptionTitle");
                    photoDescHeader.text = emails[i].photoDescriptionHeader;
                    var photoDesc = emailContent.Q<Label>("DescriptionTextLabel");
                    photoDesc.text = emails[i].photoDescription;
                }
                else
                {
                    emailContent = mailContent.CloneTree();
                }

                if (emailContent == null)
                    return;

                emailContent.AddToClassList("openedScreen");
                emailContent.style.height = Length.Percent(100);
                emailContent.style.width = Length.Percent(100);
                emailContent.Q<Label>("EmailHeader").text = emails[i].subject;
                emailContent.Q<Label>("EmailBody").text = emails[i].body;

                var backButton = emailContent.Q("Back") as Button;
                if (backButton != null)
                    backButton.clicked += ExitMailItemRPC;
                EmailContent.Insert(0, emailContent);
                emails[i].isRead = true;
            }
        }
        // update rooster tails
        bool anyUnread = false;
        for (int i = 0; i < emails.Length; i++)
        {
            if (emails[i] != null && !emails[i].isRead)
                anyUnread = true;
        }
        var mailNew = document?.rootVisualElement?.Q("Email")?.Q("New");
        if (mailNew != null)
            mailNew.visible = anyUnread;

        PlaySFX("Select");
        //PlaySFX("OpenEmail");
    }

    [Rpc(SendTo.Everyone)]
    private void ExitMailItemRPC()
    {
        RPC.Log(this);

        var mailItem = document?.rootVisualElement?.Q(className: "openedScreen");
        if (mailItem != null)
        {
            EmailContent.Remove(mailItem);
        }
        mitchAudioInstance.setPaused(true);
        ResetMailList();
        PlaySFX("Exit");
    }

    [Rpc(SendTo.Everyone)]
    private void VideoPlayRPC()
    {
        RPC.Log(this);
        if (videoPlayer != null && !videoPlayer.isPlaying)
            videoPlayer.Play();
        if (!mitchAudioInstance.isValid())
        {
            Debug.LogWarning("Play: Mitch Audio Instance not init-ed");
            return;
        }

        mitchAudioInstance.getPlaybackState(out PLAYBACK_STATE playbackState);

        bool isPaused;
        mitchAudioInstance.getPaused(out isPaused);

        if (playbackState == PLAYBACK_STATE.PLAYING && isPaused)
        {
            // Resume playback if currently paused
            mitchAudioInstance.setPaused(false);
        }
        else if (playbackState != PLAYBACK_STATE.PLAYING)
        {
            // Start from the beginning if not currently playing
            mitchAudioInstance.start();
        }
    }

    [Rpc(SendTo.Everyone)]
    private void VideoPauseRPC()
    {
        RPC.Log(this);
        if (videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.Pause();
        if (!mitchAudioInstance.isValid())
        {
            Debug.LogWarning("Pause: Mitch Audio Instance not init-ed");
            return;
        }

        mitchAudioInstance.getPlaybackState(out PLAYBACK_STATE playbackState);
        if (playbackState == PLAYBACK_STATE.PLAYING)
        {
            mitchAudioInstance.setPaused(true);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void VideoRestartRPC()
    {
        RPC.Log(this);
        if (videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.frame = 0;
        if (!mitchAudioInstance.isValid())
        {
            Debug.LogWarning("Restart: Mitch Audio Instance not init-ed");
            return;
        }
        mitchAudioInstance.start();
    }

    // Callback function to handle event stopped
    [AOT.MonoPInvokeCallback(typeof(EVENT_CALLBACK))]
    private static FMOD.RESULT OnEventCallback(EVENT_CALLBACK_TYPE type, IntPtr _event, IntPtr parameters)
    {
        if (type == EVENT_CALLBACK_TYPE.TIMELINE_MARKER)
        {
            // Retrieve the marker information
            TIMELINE_MARKER_PROPERTIES marker = (TIMELINE_MARKER_PROPERTIES)System.Runtime.InteropServices.Marshal.PtrToStructure(parameters, typeof(TIMELINE_MARKER_PROPERTIES));
            OnTimelineMarker(marker.name);
        }
        return FMOD.RESULT.OK;
    }
    // Function to handle what happens when the event finishes
    private static void OnTimelineMarker(string markerName)
    {
        Debug.Log("Timeline Marker Reached: " + markerName);

        // Place your custom actions here, based on the marker name
        if (markerName == "VideoEnd")
        {
            //VideoPauseRPC();
        }
    }

    [Rpc(SendTo.Everyone)]
    private void ToggleTranscriptRPC()
    {
        RPC.Log(this);
        var transcriptButton = EmailContent.Q<Button>("TranscriptButton");
        var transcriptText = EmailContent.Q("TranscriptText");
        if (transcriptText != null)
        {
            if (transcriptText.visible)
            {
                transcriptText.visible = false;
                transcriptButton.style.rotate = new StyleRotate(new UnityEngine.UIElements.Rotate(90));
            }
            else
            {
                transcriptText.visible = true;
                transcriptButton.style.rotate = new StyleRotate(new UnityEngine.UIElements.Rotate(-90));
            }
        }
    }

    // gaping security/integrity hole #TIMEBOMB#
    //[Obsolete?]
    [Rpc(SendTo.Server)]
    public void _AddCurrencyToPlayerRPC(string authID, int amount)
    {
        if (!GameConfig.GameCore.IsSteamDemo) { return; } // sever if not in demo so re-arch is addressed
        if (!NetMgr.IsServer) { return; }

        RPC.Log(this);

        CurrencyManager._AdjustCurrencyForPlayer(authID, amount);

    }
    
    // gaping security/integrity hole #TIMEBOMB#
    //[Obsolete?]
    [Rpc(SendTo.Server)]
    public void _RemoveCurrencyFromPlayerRPC(string authID, int amount)
    {
        if (!GameConfig.GameCore.IsSteamDemo) { return; } // sever if not in demo so re-arch is addressed
        if (!NetMgr.IsServer) { return; }

        RPC.Log(this);

        CurrencyManager._AdjustCurrencyForPlayer(authID, -amount);
    }

    //private void AcceptContract(EventBase obj)
    //{
    //    var button = obj?.target as Button;
    //    AcceptContractRPC(button.userData as string);
    //}

    //[Rpc(SendTo.Everyone)]
    //private void AcceptContractRPC(string ID)
    //{
    //    RPC.Log(this);

    //    if (ID == null || scrollViewItem == null || emails == null || document == null)
    //    {
    //        Debug.LogError("ID, scrollViewItem, emails, or document is null.");
    //        return;
    //    }

    //    Button button = FindButtonWithUserData(ID) ?? throw new NullReferenceException("button is null.");
    //    if (button != null)
    //    {
    //        button.style.visibility = Visibility.Hidden;
    //        VisualElement newContractItem = scrollViewItem.CloneTree();
    //        var newContractItemButton = newContractItem.Q<Button>();
    //        newContractItemButton.text = emails[currentEmail].contract.subject;
    //        newContractItemButton.clickable.clickedWithEventInfo += OnContractItemClicked;
    //        newContractItemButton.userData = "ContractItem" + ContractsList.contentContainer.childCount;
    //        emails[currentEmail].contract.isRead = false;
    //        emails[currentEmail].isAccepted = true;
    //        ContractsList.Add(newContractItem);
    //        var contractNewNotif = document.rootVisualElement.Q("Contracts").Q("New") as Label;
    //        if (contractNewNotif != null)
    //            contractNewNotif.visible = true;
    //        PlaySFX("StartMission");
    //    }
    //}
    #endregion

    #region Contract Tab

    private void ContractsEnter()
    {
        ResetContractList();
        TabEnter(Contracts, ContractsButton);
    }

    private void ResetContractList()
    {
        ContractsList.Clear();
        for (int i = 0; i < contracts.Length; i++)
        {
            var newMailItem = scrollViewItem.CloneTree();
            if (newMailItem != null)
            {
                var listItemSlot = newMailItem.Q<VisualElement>("ListItemSlot");
                listItemSlot.style.backgroundColor = i % 2 == 0 ? evenColor : oddColor;
                var newMailItemButton = newMailItem.Q<Button>();
                if (!contracts[i].isRead)
                {
                    var newLabel = listItemSlot.Q<VisualElement>("ListItemLeft");
                    newLabel.style.visibility = Visibility.Visible;
                }
                var subjectLabel = listItemSlot.Q<VisualElement>("ListItemCenter").Q<Label>();
                subjectLabel.text = contracts[i].subject;
                newMailItemButton.userData = "Contract" + i;
                newMailItemButton.clickable.clickedWithEventInfo += OnContractItemClicked;
                ContractsList.Add(newMailItem);
            }
        }
        for (int i = contracts.Length; i < maxContracts; i++)
        {
            var newMailItem = scrollViewItem.CloneTree();
            if (newMailItem != null)
            {
                var listItemSlot = newMailItem.Q<VisualElement>("ListItemSlot");
                listItemSlot.style.backgroundColor = i % 2 == 0 ? evenColor : oddColor;
                var newMailItemButton = newMailItem.Q<Button>();
                listItemSlot.Remove(newMailItemButton);
                ContractsList.Add(newMailItem);
            }
        }
    }

    private void ContractsExit()
    {
        ExitContractsItemRPC();
        TabExit(Contracts, ContractsButton);
    }

    public delegate void OnContractStarted();
    public static OnContractStarted OnContractStartedObject;

    private void OnContractItemClicked(EventBase obj)
    {
        var button = (Button)obj.target;
        string ID = button.userData as string;
        OnContractItemClickedRPC(ID);
        OnContractStartedObject?.Invoke();
    }

    [Rpc(SendTo.Everyone)]
    private void OnContractItemClickedRPC(string ID)
    {
        RPC.Log(this);

        if (string.IsNullOrEmpty(ID))
        {
            Debug.LogError("Button's userData is null or empty.");
            return;
        }
        ContractsList.visible = false;
        ContractsList.style.display = DisplayStyle.None;
        for (int i = 0; i < ContractsList.childCount; i++)
        {
            if (ContractsList?.ElementAt(i)?.Q<Button>()?.userData as string == ID)
            {
                var newContractItem = contractContent.CloneTree();
                newContractItem.style.height = Length.Percent(100);
                newContractItem.style.width = Length.Percent(100);

                var contractTitle = newContractItem.Q("ContractTitle") as Label;
                contractTitle.text = contracts[i].subject;
                var contractBody = newContractItem.Q("ContractBody") as Label;
                contractBody.text = contracts[i].body;


                var start = newContractItem.Q("ACCEPT") as Button;
                if (!contracts[i].isAccepted)
                {
                    start.clicked += () =>
                    {
                        StartContractRpc(Local.Player.PlayerBrain);
                    };
                }
                else
                {
                    start.style.visibility = Visibility.Hidden;
                }
                start.userData = "ContractStart";
                var exit = newContractItem.Q("CLOSE") as Button;
                exit.clicked += ExitContractsItemRPC;
                newContractItem.AddToClassList("openedScreen");
                ContractContentElement.Add(newContractItem);
                contracts[i].isRead = true;
            }
        }
        // update roostertails
        bool anyUnread = false;
        for (int i = 0; i < contracts.Length; i++)
        {
            if (!contracts[i].isRead)
            {
                anyUnread = true;
                break;
            }
        }
        var contractNewNotif = document.rootVisualElement.Q("Contracts").Q("New");
        if (contractNewNotif != null)
            contractNewNotif.visible = anyUnread;

        PlaySFX("Select");
        //PlaySFX("OpenEmail");

    }

    [Rpc(SendTo.Everyone)]
    public void StartContractRpc(NetworkBehaviourReference brain)
    {
        RPC.Log(this);

        Debug.Log("Starting Contract");
        var startButton = FindButtonWithUserData("ContractStart");
        startButton.style.visibility = Visibility.Hidden;
        contracts[0].isAccepted = true;  //TODO: UPDATE THIS TO BE REAL
        gameObject.GetComponent<ToggleVisibleOnInteraction>()?.SecondaryInteract((BaseBrain)brain);
        PlaySFX("StartMission");

    }

    [Rpc(SendTo.Everyone)]
    private void ExitContractsItemRPC()
    {
        RPC.Log(this);
        ResetContractList();

        var remove = document.rootVisualElement.Q(className: "openedScreen");
        if (remove != null)
        {
            ContractContentElement.Remove(remove);
        }

        if (!ContractsList.visible)
        {
            ContractsList.visible = true;
            ContractsList.style.display = DisplayStyle.Flex;
        }
        PlaySFX("Exit");
    }

    #endregion

    #region Map Tab
    private RenderTexture renderTexture;
    private void MapEnter()
    {
        TabEnter(MapContent, MapButton);

        // Create a RenderTexture
        renderTexture = new RenderTexture(1400, 800, 16, RenderTextureFormat.ARGB32);

        // Assign the RenderTexture to the camera's target texture
        if (mapCam != null)
        {
            mapCam.enabled = true;
            mapCam.targetTexture = renderTexture;
            mapCam.Render();
        }
        else
        {
            Debug.LogError("mapCam is null! Make sure the camera is assigned properly.");
        }

        if (renderTexture != null)
        {
            MapView.style.backgroundImage = Background.FromRenderTexture(renderTexture);
        }
        else
        {
            Debug.LogError("Failed to convert RenderTexture to Texture2D");
        }

        for (int i = 0; i < Player.Players.Count; i++)
        {
            if (i >= 4)
            {
                Debug.LogError("[RVComputer][Map] Found more players that player slots");
                return;
            }
            var player = Player.Players[i];
            if (player == null)
            {
                Debug.LogError($"Unable to retrieve player {i}");
                continue;
            }
            var playerStatus = MapPlayerStatusContainer.ElementAt(i);
            if (playerStatus == null) continue;
            playerStatus.style.visibility = Visibility.Visible;
            var playerNameLabel = playerStatus.Q<Label>("PlayerTitleLabel");
            if (playerNameLabel != null)
            {
                if(PlayerNetworkData.TryGetPlayerDisplayName(player, out string displayName))
                {
                    playerNameLabel.text = displayName;
                }
            }
        }
    }

    private IEnumerator MapUpdate()
    {
    Loop:
        foreach (var player in Player.Players)
        {
            if (player == null) continue;

            if (player.ControlledCharacter.TryGetComponent(out CharacterStatus status) && status.HasStat(Stat.Endurance))
            {
                if (player.GetPlayerSlot() > 3) continue; // out of range
                //calculate percentage
                CharacterStat health = status.GetStat(Stat.Endurance);
                float percentage = (health.Current.Value - health.Min.Value) / (health.Max.Value - health.Min.Value) * 100f;
                // get player status healthbar container
                var playerStatus = MapPlayerStatusContainer.ElementAt(player.GetPlayerSlot());
                var healthbar = playerStatus.Q<VisualElement>("PlayerHealthBar");
                //set width
                healthbar.style.width = Length.Percent(percentage);
            }
        }
        yield return null;
        goto Loop;
    }

    private void MapExit()
    {
        Debug.Log("MapExit called");
        if (mapCam != null)
        {
            mapCam.enabled = false;
        }
        TabExit(MapContent, MapButton);
    }
    #endregion

    #region Support Tab
    private CameraTarget cameraTarget;
    private void SupportEnter()
    {
        TabEnter(Support, SupportButton);
        CameraScrollView.Clear();
        for (int i = 0; i < CameraMarker.List.Count; i++)
        {
            var newCameraItem = scrollViewItem.CloneTree();
            if (newCameraItem != null)
            {
                var newCameraItemButton = newCameraItem.Q<Button>();
                string DisplayName = CameraMarker.List[i].DisplayName;
                var listItemSlot = newCameraItem.Q<VisualElement>("ListItemSlot");
                listItemSlot.style.backgroundColor = i % 2 == 0 ? evenColor : oddColor;
                if (DisplayName == "" || DisplayName == null)
                {
                    CameraMarker.List[i].DisplayName = "Camera" + i;
                }
                var subjectLabel = newCameraItem.Q<VisualElement>("ListItemCenter").Q<Label>();
                subjectLabel.text = CameraMarker.List[i].DisplayName;
                newCameraItemButton.userData = "Camera " + i;
                newCameraItemButton.clickable.clickedWithEventInfo += OnCameraItemClicked;
                CameraScrollView.Add(newCameraItem);
                Common.Log.Info($"Security Camera: Added to list: {CameraMarker.List[i].DisplayName}");
            }
        }
        for (int i = CameraMarker.List.Count; i < maxCameras; i++)
        {
            var newMailItem = scrollViewItem.CloneTree();
            if (newMailItem != null)
            {
                var listItemSlot = newMailItem.Q<VisualElement>("ListItemSlot");
                listItemSlot.style.backgroundColor = i % 2 == 0 ? evenColor : oddColor;
                var newMailItemButton = newMailItem.Q<Button>();
                listItemSlot.Remove(newMailItemButton);
                CameraScrollView.Add(newMailItem);
            }
        }
    }

    private void SupportExit()
    {
        TabExit(Support, SupportButton);
        if (cameraTarget != null)
        {
            CameraFrame.Remove(cameraTarget);
            cameraTarget = null;
        }
        CameraScrollView.Clear();
    }

    private void OnCameraItemClicked(EventBase obj)
    {
        var button = obj?.target as Button;
        if (button != null)
        {
            OnCameraItemClickedRPC(button.userData as string);
        }
    }
    [Rpc(SendTo.Everyone)]
    private void OnCameraItemClickedRPC(string ID)
    {
        RPC.Log(this);

        Button button = FindButtonWithUserData(ID) ?? throw new NullReferenceException("button is null.");
        var subjectLabel = button.Q<VisualElement>("ListItemCenter").Q<Label>();
        var DisplayName = subjectLabel.text;
        for (int i = 0; i < CameraMarker.List.Count; i++)
        {
            if (CameraMarker.List[i].enabled == true && CameraMarker.List[i].DisplayName == DisplayName)
            {
                if(cameraTarget != null)
                {
                    CameraFrame.Remove(cameraTarget);
                    cameraTarget = null;
                }
                cameraTarget = new()
                {
                    Target = CameraMarker.List[i].Target
                };
                Common.Log.Info($"Security Camera: Updated cameraTarget for {CameraMarker.List[i].DisplayName} with Target: {cameraTarget.Target}");
                CameraFrame.Add(cameraTarget);
            }
        }
    }

    #endregion

    #region RV Tab
    private void RVEnter()
    {
        TabEnter(RVContent, RVButton);
    }
    private void RVExit()
    {
        TabExit(RVContent, RVButton);
    }


    private IEnumerator RVUpdate()
    {
        if (RV.Instance.TryGetComponent(out CharacterStatus status))
        {
        Loop:if (status && status.HasStat(Stat.Endurance))
            {
                CharacterStat health = status.GetStat(Stat.Endurance);
                float percentage = (health.Current.Value - health.Min.Value) / (health.Max.Value - health.Min.Value) * 100f;
                RVHealthBar.style.width = Length.Percent(percentage);
                RVHealthPercentageLabel.text = $"{Mathf.CeilToInt(percentage)}%";
            }
            yield return new WaitForSeconds(0.5f);
            goto Loop;
        }
    }
    #endregion

    #region Store Tab
    private void PopulateStore(ScrollView scrollView, System.Action<EventBase> onItemClicked)
    {
        // Generate item display data for each item
        int rows = scrollView.contentContainer.childCount;
        int columns = scrollView.ElementAt(0).childCount;
        int numberOfItems = InventoryInfo.InfoList.Count;
        int currentItem = 0;

        for (int j = 0; j < rows; j++)
        {
            if (currentItem >= numberOfItems) break;
            var storeRow = scrollView.contentContainer.ElementAt(j);
            if (storeRow == null)
            {
                Debug.LogError($"[RVComputer] Failed to access StoreRow at index {j}");
                return;
            }

            for (int i = 0; i < columns; i++)
            {
                if (currentItem >= numberOfItems) break;
                var storeItemSlot = storeRow.ElementAt(i);
                if (storeItemSlot == null)
                {
                    Debug.LogError($"[RVComputer] Failed to access StoreItemSlot at index {i}");
                    continue;
                }

                //display available items
                VisualElement storeItem;
                if (storeItemSlot.Q<Button>() != null)
                {
                    storeItem = storeItemSlot.Q<Button>();
                    var newStoreItemButton = storeItem.Q<Button>("StoreItemButton");
                    if (newStoreItemButton != null)
                    {
                        newStoreItemButton.style.backgroundImage = new StyleBackground(InventoryInfo.InfoList[currentItem].displayData.DisplayIcon);
                        newStoreItemButton.userData = InventoryInfo.InfoList[currentItem].GUID;
                        newStoreItemButton.visible = true;
                        newStoreItemButton.Q<Label>("Price").text = InventoryInfo.InfoList[currentItem].price.ToString();
                    }
                }
                else
                {
                    storeItem = storeItemButton.CloneTree();
                    storeItem.AddToClassList("storeItemButton");
                    var newStoreItemButton = storeItem.Q<Button>("StoreItemButton");
                    if (newStoreItemButton != null)
                    {
                        newStoreItemButton.clickable.clickedWithEventInfo += onItemClicked;
                        newStoreItemButton.style.backgroundImage = new StyleBackground(InventoryInfo.InfoList[currentItem].displayData.DisplayIcon);
                        newStoreItemButton.userData = InventoryInfo.InfoList[currentItem].GUID;
                        newStoreItemButton.Q<VisualElement>("DefaultImage").style.visibility = Visibility.Hidden;
                        newStoreItemButton.Q<Label>("Price").text = InventoryInfo.InfoList[currentItem].price.ToString();
                    }
                    storeItemSlot.Add(storeItem);
                    Debug.Log($"[RVComputer] Added the store item to StoreItemSlot at index {i}, GUID: {newStoreItemButton.userData}");
                }

                currentItem++;
            }
        }
    }

    private IEnumerator StoreUpdate()
    {
    Loop: Label MoneyElement = StoreContent.Q<Label>("Money");
        MoneyElement.text = CurrencyManager.LoadCurrency().ToString();
        yield return new WaitForSeconds(0.5f);
        goto Loop;
    }

    [Rpc(SendTo.Everyone)]
    private void AddItemToStoreRPC(string SVID, string ID)
    {
        RPC.Log(this);

        var button = FindButtonWithUserData(ID);
        var scrollView = FindScrollViewWithUserData(SVID);
        if (button == null) return;
        var newItem = button.parent;
        newItem.RemoveFromHierarchy();
        int rows = scrollView.contentContainer.childCount;
        int columns = scrollView.ElementAt(0).childCount;

        for (int j = 0; j < rows; j++)
        {
            var storeRow = scrollView.contentContainer.ElementAt(j);
            for (int i = 0; i < columns; i++)
            {
                var storeItemSlot = storeRow.ElementAt(i);
                if (storeItemSlot.Q<Button>() != null)
                {
                    continue;
                }
                else
                {
                    storeItemSlot.Add(newItem);
                    return;
                }
            }
        }
        return;
    }
    [Rpc(SendTo.Everyone)]
    private void RemoveItemFromStoreRPC(string id)
    {
        RPC.Log(this);

        var button = FindButtonWithUserData(id);
        if (button == null) return;
        VisualElement buttonContainer = button.parent as VisualElement;
        buttonContainer.parent.Remove(buttonContainer);
        InventoryInfo.needsUpdate = true;
    }

    private void StoreEnter()
    {
        InventoryInfo.needsUpdate = true;
        ResetStore();
        PopulateStore(StoreScrollView, OnStoreItemClicked);
        TabEnter(StoreContent, StoreButton);
    }

    private void ResetStore()
    {

        //clear store
        int rows = StoreScrollView.contentContainer.childCount;
        int columns = StoreScrollView.ElementAt(0).childCount;

        for (int j = 0; j < rows; j++)
        {
            var storeRow = StoreScrollView.contentContainer.ElementAt(j);
            if (storeRow == null)
            {
                Debug.LogError($"[RVComputer] Failed to clear StoreRow at index {j}");
                return;
            }

            for (int i = 0; i < columns; i++)
            {
                var storeItemSlot = storeRow.ElementAt(i);
                var storeItemButton = storeItemSlot.Q<Button>();
                if (storeItemButton != null)
                {
                    storeItemSlot.Remove(storeItemButton.parent);
                    storeItemButton = null;
                }
            }
        }

        //clear cart
        var slotRow = CartScrollView.contentContainer.ElementAt(0);
        int slotCount = slotRow.childCount;
        for (int i = 0; i < slotCount; i++)
        {
            var currSlotButton = slotRow.ElementAt(i).Q<Button>();
            if (currSlotButton != null)
            {
                slotRow.ElementAt(i).Remove(currSlotButton.parent);
            }
        }

    }

    private void StoreExit()
    {
        // Log when exiting the store
        TabExit(StoreContent, StoreButton);
    }

    void OnStoreItemClicked(EventBase obj)
    {
        var button = obj.target as Button;
        button.clickable.clickedWithEventInfo -= OnStoreItemClicked;
        button.clickable.clickedWithEventInfo += OnCartItemClicked;
        AddItemToStoreRPC(CartScrollView.userData as string, button.userData as string);
        PlaySFX("Select");
    }
    void OnCartItemClicked(EventBase obj)
    {
        var button = obj.target as Button;
        button.clickable.clickedWithEventInfo += OnStoreItemClicked;
        button.clickable.clickedWithEventInfo -= OnCartItemClicked;
        AddItemToStoreRPC(StoreScrollView.userData as string, button.userData as string);
        PlaySFX("Select");
    }
    private void PurchaseItemsInCart()
    {
        var slotRow = CartScrollView.contentContainer.ElementAt(0);
        int slotCount = slotRow.childCount;
        for (int i = 0; i < slotCount; i++)
        {
            var currSlotButton = slotRow.ElementAt(i).Q<Button>();
            if (currSlotButton != null)
            {
                var containers = Local.Player.ControlledCharacter.transform.FindDescendantsOfType<InventoryAuthoring>();
                InventoryAuthoring bag = containers[0]; //backpack
                if (BuyFrom_StoreContainer.Count<ItemAuthoring>((string)currSlotButton.userData) > 0)
                {
                    int price = (int)uint.Parse(currSlotButton.Q<Label>("Price").text);
                    if (CurrencyManager.ClientAllowedToSpendCurrency(price))
                    {
                        _RemoveCurrencyFromPlayerRPC(Auth.PlayerID, price);
                        BuyFrom_StoreContainer.RequestTransfer((string)currSlotButton.userData, 1, bag);
                        RemoveItemFromStoreRPC(currSlotButton.userData as string);
                    }
                }
            }
        }
        PlaySFX("SellLoot");
    }
    #endregion

    #region Inventory Tab
    private List<ItemAuthoring> GetItemsFromInventory()
    {
        List<ItemAuthoring> items = new List<ItemAuthoring>();
        var containers = Local.Player.ControlledCharacter.transform.FindDescendantsOfType<InventoryAuthoring>();
        for (var i = 0; i < containers.Count; ++i)
        {
            var child = containers[i];
            for (var j = 0; j < child.Stacks.Count; ++j)
            {
                var stack = child.Stacks[j];
                var item = stack.getItem();
                if (item != null)
                    items.Add(item);
            }
        }
        return items;
    }

    private IEnumerator InventoryUpdate()
    {
        Loop: Label MoneyElement = InventoryContent.Q<Label>("Money");
        MoneyElement.text = CurrencyManager.LoadCurrency().ToString();
        yield return new WaitForSeconds(0.5f);
        goto Loop;
    }
    private void PopulateStore(ScrollView scrollView, List<ItemAuthoring> items, System.Action<EventBase> onItemClicked)
    {
        // Generate item display data for each item
        List<ComputerItemData> displayDatas = new List<ComputerItemData>();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null) continue;
            var currItem = items[i];
            StartCoroutine(Items.Inventory.GetItemDisplayData(currItem.DisplayData, itemData =>
            {
                if (itemData == null)
                {
                    Debug.LogError("[RVComputer] UpdateButtonDetails: itemData is null");
                    return;
                }
                ComputerItemData newItemData = new ComputerItemData();
                newItemData.displayData = itemData;
                newItemData.GUID = currItem.GUID;
                newItemData.price = currItem.Value;
                displayDatas.Add(newItemData);
            }));
        }

        int rows = scrollView.contentContainer.childCount;
        int columns = scrollView.ElementAt(0).childCount;
        int numberOfItems = displayDatas.Count;
        int currentItem = 0;

        for (int j = 0; j < rows; j++)
        {
            if (currentItem >= numberOfItems) break;
            var storeRow = scrollView.contentContainer.ElementAt(j);
            if (storeRow == null)
            {
                Debug.LogError($"[RVComputer] Failed to access StoreRow at index {j}");
                return;
            }

            for (int i = 0; i < columns; i++)
            {
                if (currentItem >= numberOfItems) break;

                var storeItemSlot = storeRow.ElementAt(i);
                if (storeItemSlot == null)
                {
                    Debug.LogError($"[RVComputer] Failed to access StoreItemSlot at index {i}");
                    continue;
                }

                VisualElement storeItem;
                if (storeItemSlot.Q<Button>() != null)
                {
                    storeItem = storeItemSlot.Q<Button>();
                    var newStoreItemButton = storeItem.Q<Button>("StoreItemButton");
                    if (newStoreItemButton != null)
                    {
                        newStoreItemButton.style.backgroundImage = new StyleBackground(displayDatas[currentItem].displayData.DisplayIcon);
                        newStoreItemButton.userData = displayDatas[currentItem].GUID;
                        newStoreItemButton.Q<Label>("Price").text = displayDatas[currentItem].price.ToString();
                        newStoreItemButton.visible = true;
                    }
                }
                else
                {
                    storeItem = storeItemButton.CloneTree();
                    storeItem.AddToClassList("storeItemButton");
                    var newStoreItemButton = storeItem.Q<Button>("StoreItemButton");
                    if (newStoreItemButton != null)
                    {
                        newStoreItemButton.clickable.clickedWithEventInfo += onItemClicked;
                        newStoreItemButton.style.backgroundImage = new StyleBackground(displayDatas[currentItem].displayData.DisplayIcon);
                        newStoreItemButton.userData = displayDatas[currentItem].GUID;
                        newStoreItemButton.Q<Label>("Price").text = displayDatas[currentItem].price.ToString();
                        newStoreItemButton.Q<VisualElement>("DefaultImage").style.visibility = Visibility.Hidden;
                    }
                    storeItemSlot.Add(storeItem);
                }

                currentItem++;
            }
        }
    }
    private void InventoryEnter()
    {
        List<ItemAuthoring> items = GetItemsFromInventory();
        PopulateStore(InventoryScrollView, items, OnInventoryItemClicked);
        TabEnter(InventoryContent, InventoryButton);
    }

    private void InventoryExit()
    {
        // Log when exiting the store
        TabExit(InventoryContent, InventoryButton);
    }

    //void OnInventoryItemClicked(EventBase obj)
    //{
    //    // Log the button click event
    //    var button = obj.target as Button;
    //    if (button != null)
    //    {
    //        if (SellTo_StoreContainer != null)
    //        {
    //            var containers = Local.Player.ControlledCharacter.transform.FindDescendantsOfType<InventoryAuthoring>();
    //            for (var i = 0; i < containers.Count; ++i)
    //            {
    //                InventoryAuthoring bag = containers[i];
    //                if (bag.Count<ItemAuthoring>((string)button.userData) > 0)
    //                {
    //                    bag.RequestTransfer((string)button.userData, 1, SellTo_StoreContainer);
    //                    int price = (int)uint.Parse(button.Q<Label>("Price").text);
    //                    CurrencyManager.AddCurrency(price);
    //                }
    //            }
    //        }
    //        else
    //        {
    //            Debug.LogError("[RVComputer][Inventory] NO GARBAGE CAN IN THE SCENE");
    //        }
    //    }
    //    else
    //    {
    //        Debug.LogError("[RVComputer][Inventory] Failed to cast target to Button");
    //    }

    //    PlaySFX("SellLoot");
    //}
    void OnInventoryItemClicked(EventBase obj)
    {
        var button = obj.target as Button;
        button.clickable.clickedWithEventInfo -= OnInventoryItemClicked;
        button.clickable.clickedWithEventInfo += OnInvenCartItemClicked;
        AddItemToInvenCart(InvenCartScrollView, button);
        PlaySFX("Select");
    }

    void OnInvenCartItemClicked(EventBase obj)
    {
        var button = obj.target as Button;
        button.clickable.clickedWithEventInfo -= OnInvenCartItemClicked;
        button.clickable.clickedWithEventInfo += OnInventoryItemClicked;
        AddItemToInventoryScrollView(InventoryScrollView, button);
        PlaySFX("Select");
    }

    private void AddItemToInvenCart(ScrollView invenCartScrollView, Button itemButton)
    {
        var itemContainer = itemButton.parent;
        itemContainer.RemoveFromHierarchy();

        int rows = invenCartScrollView.contentContainer.childCount;
        int columns = invenCartScrollView.ElementAt(0).childCount;
        bool slotFound = false;

        for (int j = 0; j < rows; j++)
        {
            var row = invenCartScrollView.contentContainer.ElementAt(j);
            for (int i = 0; i < columns; i++)
            {
                var slot = row.ElementAt(i);
                if (slot.Q<Button>() == null)
                {
                    slot.Add(itemContainer);
                    slotFound = true;
                    break;
                }
            }
            if (slotFound) break;
        }

        // If no slots were found in the cart, add item back to InventoryScrollView
        if (!slotFound)
        {
            AddItemToInventoryScrollView(InventoryScrollView, itemButton);
            Debug.LogWarning("No available slots in inventory cart. Returning item to inventory.");
        }
    }
    private void AddItemToInventoryScrollView(ScrollView inventoryScrollView, Button itemButton)
    {
        var itemContainer = itemButton.parent;
        itemContainer.RemoveFromHierarchy();

        int rows = inventoryScrollView.contentContainer.childCount;
        int columns = inventoryScrollView.ElementAt(0).childCount;

        for (int j = 0; j < rows; j++)
        {
            var row = inventoryScrollView.contentContainer.ElementAt(j);
            for (int i = 0; i < columns; i++)
            {
                var slot = row.ElementAt(i);
                if (slot.Q<Button>() == null)
                {
                    slot.Add(itemContainer);
                    return;
                }
            }
        }
    }

    private void SellItemsInCart()
    {
        var slotRow = InvenCartScrollView.contentContainer.ElementAt(0);
        int slotCount = slotRow.childCount;

        for (int i = 0; i < slotCount; i++)
        {
            var currSlotButton = slotRow.ElementAt(i).Q<Button>();
            if (currSlotButton != null)
            {
                string itemGuid = currSlotButton.userData as string;
                bool itemSold = false;
                int price = int.Parse(currSlotButton.Q<Label>("Price").text);

                var containers = Local.Player.ControlledCharacter.transform.FindDescendantsOfType<InventoryAuthoring>();
                foreach (InventoryAuthoring container in containers)
                {
                    uint itemCount = container.Count<ItemAuthoring>(itemGuid);
                    if (itemCount > 0)
                    {
                        container.RequestTransfer(itemGuid, 1, SellTo_StoreContainer);
                        _AddCurrencyToPlayerRPC(Auth.PlayerID, price);

                        itemSold = true;
                        break;
                    }
                }

                if (itemSold)
                {
                    RemoveItemFromInvenCart(currSlotButton);
                }
                else
                {
                    Debug.LogWarning($"Item with GUID {itemGuid} not found in any container.");
                }
            }
        }
        PlaySFX("SellLoot");
    }

    private void RemoveItemFromInvenCart(Button itemButton)
    {
        var itemContainer = itemButton.parent;
        itemContainer.parent.Remove(itemContainer);
    }
    #endregion

    #region State Machine
    public enum Tab : byte
    {
        None = 0,
        Email = 1,
        Contracts = 2,
        Map = 3,
        Support = 4,
        RV = 5,
        Creatures = 6,
        Store = 7,
        Inventory = 8,
    }

    private void OnTabChanged(Tab previousTab, Tab newTab)
    {
        HandleClickTabClientRPC(newTab);
    }
    [Rpc(SendTo.NotServer)]
    private void HandleClickTabClientRPC(Tab tab)
    {
        RPC.Log(this);

        FSM.ActiveState = activeTab.Value;
        PlaySFX("Select");
    }

    [Rpc(SendTo.Server)]
    private void RequestTabChangeServerRpc(Tab tab)
    {
        RPC.Log(this);

        if (IsServer)
        {
            HandleClickTab(tab);
        }
    }

    private void HandleClickTab(Tab tab)
    {
        if (IsServer)
        {
            Tab previous = FSM.ActiveState;
            FSM.ActiveState = previous == tab ? Tab.None : tab;
            activeTab.Value = FSM.ActiveState;
        }
    }

    private void TabEnter(VisualElement tab, Button tabButton)
    {
        if (tab.ClassListContains("contentListOff"))
            tab.RemoveFromClassList("contentListOff");
        if (!tabButton.ClassListContains("tabButtonsClicked"))
            tabButton.AddToClassList("tabButtonsClicked");
        PlaySFX("MenuMove");
    }
    private void TabExit(VisualElement tab, Button tabButton)
    {
        if (!tab.ClassListContains("contentListOff"))
            tab.AddToClassList("contentListOff");
        if (tabButton.ClassListContains("tabButtonsClicked"))
            tabButton.RemoveFromClassList("tabButtonsClicked");
        PlaySFX("MenuMove");
    }

    private void ClickNone() => RequestTabChangeServerRpc(Tab.None);
    private void ClickEmail() => RequestTabChangeServerRpc(Tab.Email);
    private void ClickContracts() => RequestTabChangeServerRpc(Tab.Contracts);
    private void ClickMap() => RequestTabChangeServerRpc(Tab.Map);
    private void ClickSupport() => RequestTabChangeServerRpc(Tab.Support);
    private void ClickRV() => RequestTabChangeServerRpc(Tab.RV);
    private void ClickCreatures() => RequestTabChangeServerRpc(Tab.Creatures);
    private void ClickStore() => RequestTabChangeServerRpc(Tab.Store);
    private void ClickInventory() => RequestTabChangeServerRpc(Tab.Inventory);

    [Rpc(SendTo.Everyone)]
    private void DoOnFocusRPC()
    {
        RPC.Log(this);

        if (screen != null && Tabs.ClassListContains("tabButtonsOff"))
        {
            Tabs.RemoveFromClassList("tabButtonsOff");
        }
        HideAlert();
        ShowTabButtons();
        ShowLogo();
        PlaySFX("TurnOnComputer");
    }
    [Rpc(SendTo.Everyone)]
    private void DoOnUnFocusRPC()
    {
        RPC.Log(this);

        foreach (var element in ContentPages)
        {
            if (element != null && !element.ClassListContains("contentListOff"))
            {
                element.AddToClassList("contentListOff");
            }
        }
        PlaySFX("TurnOffComputer");
    }
    private void HideTabButtons()
    {
        if (Tabs != null && !Tabs.ClassListContains("tabButtonsOff"))
        {
            Tabs.AddToClassList("tabButtonsOff");
        }
    }
    private void ShowTabButtons()
    {
        if (screen != null && Tabs.ClassListContains("tabButtonsOff"))
        {
            Tabs.RemoveFromClassList("tabButtonsOff");
        }
    }
    private void DoOnFocus()
    {
        interactionBlocker?.GetComponent<NetworkActive>().SetActive(true);
        DoOnFocusRPC();
        HideAlert();
        ShowLogo();
    }
    private void DoOnUnFocus()
    {
        interactionBlocker?.GetComponent<NetworkActive>().SetActive(false);
        DoOnUnFocusRPC();
        RequestTabChangeServerRpc(Tab.None);
    }

    private Button FindButtonWithUserData(string userDataValue)
    {
        VisualElement root = document.rootVisualElement;

        var buttons = root.Query<Button>().ToList();

        foreach (var button in buttons)
        {
            if (button.userData != null && button.userData.ToString() == userDataValue)
            {
                return button; // Return the found button
            }
        }

        return null; // Return null if no button matches
    }

    private ScrollView FindScrollViewWithUserData(string userDataValue)
    {
        VisualElement root = document.rootVisualElement;

        var buttons = root.Query<ScrollView>().ToList();

        foreach (var button in buttons)
        {
            if (button.userData != null && button.userData.ToString() == userDataValue)
            {
                return button; // Return the found scroll view
            }
        }

        return null; // Return null if no button matches
    }
    #endregion

    #region SFX
    private void SetupSFX()
    {
        if (sfxEvents == null) return;
        if(FMODUtil._IgnoreFMODEvents()) { return; }

        fmodEvents = new Dictionary<string, FMOD.Studio.EventInstance>();
        //EventReference  TODO use event references instead of string paths
        for (int i = 0; i < sfxEvents.Length; i++)
        {
            FMOD.Studio.EventInstance newInstance = RuntimeManager.CreateInstance(sfxEvents[i].eventPath);
            newInstance.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject));
            fmodEvents.Add(sfxEvents[i].eventName, newInstance);
        }

        var clickableElements = document.rootVisualElement.Query<Button>().ToList();
        foreach (var element in clickableElements)
        {
            element.RegisterCallback<MouseEnterEvent>(OnHover);
        }
    }
    private void PlaySFX(string eventName)
    {
        //return;
        if (FMODUtil._IgnoreFMODEvents()) { return; }

        if (fmodEvents.TryGetValue(eventName, out var eventInstance))
        {
            eventInstance.start();
        }
        else
        {
            Debug.LogWarning($"FMOD event '{eventName}' not found.");
        }
    }
    private void OnHover(MouseEnterEvent e)
    {
        PlaySFX("Hover");
    }
    #endregion

}
