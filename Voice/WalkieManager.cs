using PLI.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using CustomInspectorAttributes.Rewrite.Character;
using PLI.Net;
using PLI.ScreenFlow;
using PLI.Lobbies;
using PLI.Dev;
using System.Collections.Generic;
using PLI.ECS.Game;



public class WalkieManager : NetworkBehaviour
{
    private static WalkieManager s_Singleton;
    public static WalkieManager Instance => s_Singleton;
    static private LogChannelShim Log => PLI.Logging.Core.Channels.Vivox;

    public NetworkVariable<FixedString64Bytes> currentSpeaker = new NetworkVariable<FixedString64Bytes>();

    public FixedString64Bytes unset = "UNSET";

    double _StartTS = 0;           // timestamps should be doubles (from epoch, needs the granularity), durations/deltas are fine as floats - JKB 
    double _LastPosUpdateTS = 0;
        
    float posUpdateFreq = 0.15f;

    private void OnEnable()
    {
        if (s_Singleton == null)
        {
            s_Singleton = this;
            currentSpeaker.Value = unset;
            _StartTS = PLI.Clocks.FromEpoch.InSecs();
        }
        else
        {
            Log.Warn("Multiple instances of WalkieManager found! Ensure only one instance exists in the project.");
        }
    }

    private void OnDisable()
    {
        if (s_Singleton == this)
        {
            s_Singleton = null;
        }
    }

    [Rpc(SendTo.Server)]
    public void SetSpeakerRpc(FixedString64Bytes newSpeaker)
    {
        //Log.Info($"Client wants to set the speaker to: {newSpeaker}");
        RequestToSetSpeakerServerRpc(newSpeaker);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToSetSpeakerServerRpc(FixedString64Bytes newSpeaker, ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        //Log.Info($"Server received request to set speaker to: {newSpeaker}");
        currentSpeaker.Value = newSpeaker;
    }

    private void Update()
    {
        if(GameConfig.Dev.AllowDevShell) 
            { _Dev_UpdatePlayerPosStatus(); }

        if (Kingdoms.InGame.IsActive )
        {
            _UpdateTapPositionsToInGameCharacters();
        }
        else
        {
            _ResetAllTapPositionsToIdentityAsNeeded();
        }
    }

    private void _Dev_UpdatePlayerPosStatus()
    {
        List<string> playerPositions = new();
        foreach (var cur in Player.Players)
        {
            //Vector3 curPlayerPos = (cur.gameObject != null) ? cur.gameObject.transform.position : Vector3.zero;
            Vector3 curCharPos = (cur.gameObject != null && cur.ControlledCharacter != null) ? cur.ControlledCharacter.transform.position : Vector3.zero;
            //playerPositions.Add($"P:{curPlayerPos.ToString()}, C:{curCharPos.ToString()}");
            playerPositions.Add($"C:{curCharPos.ToString()}");
        }

        string playerAPos = (playerPositions.Count >= 1) ? playerPositions[0] : "-";
        string playerBPos = (playerPositions.Count >= 2) ? playerPositions[1] : "-";
        string playerCPos = (playerPositions.Count >= 3) ? playerPositions[2] : "-";
        string playerDPos = (playerPositions.Count >= 4) ? playerPositions[3] : "-";

        _Dev.Status.Set("PlayerA", playerAPos, "Players");
        _Dev.Status.Set("PlayerB", playerBPos, "Players");
        _Dev.Status.Set("PlayerC", playerCPos, "Players");
        _Dev.Status.Set("PlayerD", playerDPos, "Players");

        _Dev.Status.Set("TapPos", "--", "Players");
    }                              

    private void _UpdateTapPositionsToInGameCharacters()
    {
        //delay update of position
        float durFromLastUpdate = (_LastPosUpdateTS <= 0.0) ? 99999f : (float)PLI.Clocks.Dur.FromEpoch.InSecs(_LastPosUpdateTS);
        if (posUpdateFreq > 0.0f && durFromLastUpdate < posUpdateFreq) { return; }


        int numSet = 0;
        foreach (Player player in Player.Players)
        {
            if(player.ControlledCharacter == null) { continue; }

            if (PlayerNetworkData.TryGetPlayerAuthID(player, out var playerAuthID))
            {
                VivoxController.Instance.VivoxTaps.TryGetValue(playerAuthID, out GameObject tapObject);
                if (tapObject == null) { continue; }

                player.ControlledCharacter.transform.GetPositionAndRotation(out Vector3 currentPosition, out Quaternion currentRotation);

                // Only update if there's a difference
                if (tapObject.transform.position != currentPosition || tapObject.transform.rotation != currentRotation)
                {
                    tapObject.transform.SetPositionAndRotation(currentPosition, currentRotation);
                }
                numSet++;
            }
            else
            {
                Log.SoftWarn($"Failed to Find Auth ID for '{player.DisplayName}'");
                //Kingdoms.Dialogs.Toast("Failed to Find Auth ID", $"Name? '{player.DisplayName}'");
            }
        }

        _Dev.Status.Set("TapPos", $"C{numSet}", "Players");

        _LastPosUpdateTS = PLI.Clocks.FromEpoch.InSecs();
    }

    private void _ResetAllTapPositionsToIdentityAsNeeded()
    {
        if (VivoxController.Instance == null) { return; }

        if (VivoxController.Instance.VivoxTaps != null)
        {
            foreach (var entry in VivoxController.Instance.VivoxTaps)
            {
                GameObject tapObject = entry.Value;

                // Only update if needed
                if (tapObject.transform.position != Vector3.zero || tapObject.transform.rotation != Quaternion.identity)
                {
                    tapObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                }
            }
        }

        _Dev.Status.Set("TapPos", $"I{VivoxController.Instance.VivoxTaps?.Count??-1}", "Players");
    }
}
