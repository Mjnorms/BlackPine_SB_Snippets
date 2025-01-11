using PLI.ECS.Graphics;
using PLI.GameCore;
using PLI.GameCore.UI.Panels.Station;
using PLI.Lobbies;
using PLI.ScreenFlow;
using System;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.UIElements;

public class VivoxTextUiHandler : MonoBehaviour
{

    private UIDocument _doc = null;
    private VisualElement _textChatContainer = null;
    private ScrollView _chatHistoryStaging = null;
    private ScrollView _chatHistoryLobby = null;
    public VisualTreeAsset _labeldoc = null;

    private static VivoxTextUiHandler s_Singleton;
    public static VivoxTextUiHandler Instance => s_Singleton;

    private void OnEnable()
    {
        if (s_Singleton == null)
        {
            s_Singleton = this;
        }
        else
        {
            Debug.LogWarning("Multiple instances of VivoxUIHandler found! Ensure only one instance exists in the project.");
        }
    }

    private void Start()
    {
        _doc = GameCore_GameObj.UIRefs?.GetScreenDoc("StationScreen");
        if (_doc == null) { Kingdoms.Lobby.Log.Error("Failed to find Station Screen doc"); return; }

        _textChatContainer = _doc.rootVisualElement.Q("ChatFrame") as VisualElement;
        _chatHistoryStaging = _doc.rootVisualElement.Q("ChatHistory").Q<ScrollView>();
        var lobbyScreenDoc = GameCore_GameObj.UIRefs?.GetScreenDoc("LobbyScreen");
        _chatHistoryLobby = lobbyScreenDoc?.rootVisualElement.Q("ChatHistory").Q<ScrollView>();
    }

    private void OnDisable()
    {
        if (s_Singleton == this)
        {
            s_Singleton = null;
        }
    }

    public void SendTextMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        if (!VivoxController.Instance)
        {
            PostError("Not Logged into chat service");
            Debug.LogWarning("Attempting to send message without Vivox Instance");
            return;
        }
        if (!VivoxController.Instance.HasJoined)
        {
            PostError("Not in a chat lobby");
        }
        else
        {
            Debug.Log("Vivox: Message Sending");
            VivoxController.Instance.SendMessageAsync(message);
        }
    }

    public void PostMessage(VivoxMessage message)
    {
        if (message == null) return;

        string senderName = message.SenderDisplayName;
        string messageText = message.MessageText;
        DateTime timeReceived = message.ReceivedTime;
        bool fromSelf = message.FromSelf;
        Debug.Log("Vivox: Message Posting: " + messageText);

        var stationLabel = _labeldoc.CloneTree();
        stationLabel.Q<Label>().text = ("[" + senderName + "]: " + messageText);
        _chatHistoryStaging?.Add(stationLabel);
        var lobbyLabel = _labeldoc.CloneTree();
        lobbyLabel.Q<Label>().text = ("[" + senderName + "]: " + messageText);
        _chatHistoryLobby?.Add(lobbyLabel);

    }

    private void PostError(string message)
    {
        if (message == null) return;

        var stationLabel = _labeldoc.CloneTree();
        stationLabel.Q<Label>().text = (message);
        stationLabel.style.color = Color.red;
        _chatHistoryStaging.Add(stationLabel);
        var lobbyLabel = _labeldoc.CloneTree();
        lobbyLabel.Q<Label>().text = (message);
        lobbyLabel.style.color = Color.red;
        _chatHistoryLobby?.Add(lobbyLabel);
    }

    public void ClearChat()
    {
        _chatHistoryLobby?.Clear();
        _chatHistoryStaging?.Clear();
    }
}
