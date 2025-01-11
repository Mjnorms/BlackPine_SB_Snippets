using PLI.CharacterStats;
using PLI.Dev;
using PLI.ECS.Game;
using PLI.Extensions;
using PLI.Game.Options;
using PLI.GameCore;
using PLI.GameCore.UI.Groups;
using PLI.Logging;
using PLI.Prims;
using PLI.ScreenFlow;
using PLI.Unity_Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.Services.Vivox;
using Unity.Services.Vivox.AudioTaps;
using UnityEngine;

#if UNITY_EDITOR
    using UnityEditor;
#endif

namespace PLI.Lobbies
{
    public class _VivoxServiceEventHooks
    {
        #region Log In/Out
        /// <summary>
        /// An action that will trigger when an Account is successfully LoggedIn to the Vivox Service.
        /// </summary>

        public Action LoggedIn;

        /// <summary>
        /// An action that will trigger when an Account is successfully LoggedIn to the Vivox Service.
        /// </summary>
        public Action LoggedOut;
        #endregion

        #region Available devices changed

        /// <summary>
        /// An action triggered when the input device list in refreshed/updated.
        /// An example of when this will fire is when an input device is disconnected from the primary device.
        /// </summary>
        public Action AvailableInputDevicesChanged;

        /// <summary>
        /// An action triggered when the output device list in refreshed/updated.
        /// An example of when this will fire is when an output device is disconnected from the primary device.
        /// </summary>
        public Action AvailableOutputDevicesChanged;
        #endregion

        #region Connection Recovery
        /// <summary>
        /// An Action that will fire when the network connection for the logged-in device is interrupted.
        /// Vivox will attempt to re-establish connection for 30 seconds, firing ConnectionRecovered if the connection is recovered, or ConnectionFailedToRecover if there is a failure to reconnect.
        /// </summary>
        public Action ConnectionRecovering;

        /// <summary>
        /// An action that will fire when the network connection has been successfully recovered.
        /// </summary>
        public Action ConnectionRecovered;

        /// <summary>
        /// An Action that will fire when the network connection has been interrupted for over 30 seconds, and Vivox has halted attempts to reconnect.
        /// </summary>
        public Action ConnectionFailedToRecover;
        #endregion

        #region Join / Left Channel
        /// <summary>
        /// An action that will trigger when a Channel has been successfully joined by the currently logged in user.
        /// Once this event fires, the user will be in the selected text/audio state based on the ChatCapabilities of the channel, and will be able to do all channel actions.
        /// Provides the ChannelName of the channel successfully joined.
        /// </summary>
        public Action<string> ChannelJoined;

        /// <summary>
        /// An action that will trigger when a Channel has been successfully left by the currently logged in user.
        /// Once this event fires, the user will no longer be in the text/audio state for this channel, and will no longer be able to do any channel operations.
        /// Provides the ChannelName of the channel successfully left.
        /// </summary>
        public Action<string> ChannelLeft;
        #endregion

        #region Participant Added / Removed locally seen channel
        /// <summary>
        /// An Action that will trigger when a new Participant has been added to any channel the user is in.
        /// Provides a Participant object, which contains the Channel the participant is in, along with their PlayerId, DisplayName, whether speech has been detected, more specific audio energy changes and Muted status.
        /// </summary>
        public Action<VivoxParticipant> ParticipantAddedToChannel;

        /// <summary>
        /// An Action that will trigger when a Participant has been removed from a channel the user is in.
        /// Provides a Participant object, which contains the Channel the participant is in, along with their PlayerId, DisplayName, whether speech has been detected, more specific audio energy changes and Muted status.
        /// </summary>
        public Action<VivoxParticipant> ParticipantRemovedFromChannel;

        #endregion

        #region Channel message
        /// <summary>
        /// An Action that will trigger when a channel message has been received in any channel the user is in. The VivoxMessage itself will contain ChannelName of the channel it was sent in, and the PlayerId and DisplayName of the Sender
        /// </summary>
        public Action<VivoxMessage> ChannelMessageReceived;

        /// <summary>
        /// An Action that will trigger when a channel message has been edited in any channel the user is in. The VivoxMessage itself will contain the ChannelName of the channel it was sent in, the PlayerId of the Sender, and the MessageId that was edited.
        /// </summary>
        public Action<VivoxMessage> ChannelMessageEdited;

        /// <summary>
        /// An Action that will trigger when a channel message has been deleted in any channel the user is in. The VivoxMessage itself will contain the ChannelName of the channel it was sent in, the PlayerId of the Sender, and the MessageId that was deleted.
        /// The MessageText will be null.
        /// </summary>
        public Action<VivoxMessage> ChannelMessageDeleted;
        #endregion

        #region Direct message
        /// <summary>
        /// An Action that will trigger when a directed message has been received by the currently logged in user. The VivoxMessage itself will have the PlayerId and DisplayName of the Player, and the ChannelName will be set to null.
        /// </summary>
        public Action<VivoxMessage> DirectedMessageReceived;

        /// <summary>
        ///  An Action that will trigger when a direct message has been deleted in any channel the user is in. The VivoxMessage itself will have the PlayerId of the Player and the MessageId that was edited.
        /// The ChannelName and Message will be set to null.
        /// </summary>
        public Action<VivoxMessage> DirectedMessageDeleted;

        /// <summary>
        /// An Action that will trigger when a direct message has been deleted in any channel the user is in. The VivoxMessage itself will have the PlayerId of the Player and the MessageId that was edited.
        /// The ChannelName will be set to null.
        /// </summary>
        public Action<VivoxMessage> DirectedMessageEdited;
        #endregion

        // ---------------

        #region Custom (Post Login)
        /// <summary>
        /// Called after login sucess and devices/settings are selected.
        /// </summary>
        public Action PostLogin;
        #endregion
        // ------------

        #region Ctor / Reset
        public _VivoxServiceEventHooks()
        {
            Reset();
        }
        public void Reset()
        {
            LoggedIn = null;
            LoggedOut = null;
            AvailableInputDevicesChanged = null;
            AvailableOutputDevicesChanged = null;
            ConnectionRecovering = null;
            ConnectionRecovered = null;
            ConnectionFailedToRecover = null;
            ChannelJoined = null;
            ChannelLeft = null;
            ParticipantAddedToChannel = null;
            ParticipantRemovedFromChannel = null;

            ChannelMessageReceived = null;
            ChannelMessageEdited = null;
            ChannelMessageDeleted = null;
            DirectedMessageReceived = null;

            DirectedMessageDeleted = null;

            DirectedMessageEdited = null;
            PostLogin = null;
        }
        #endregion
    }
    public class PLIService_Vivox : Unity_Services.IService
    {
        // Settings
        public const float  kRetryDelayInSecs     = 2.0f;
        public const int    kDefaultMaxAttempts   = (1 * 60) / (int)kRetryDelayInSecs; // 1 mins of attempts
        public const string kEchoChannel          = "Echo";

        public const float Settings_WaitForLoginTimeoutInSecs = 1.2f; // todo : move to game config

        // Init & Hooks
        public bool IsInitialized { get { return _Initd; } }
        public bool InitStarted => _InitStarted;

        public _VivoxServiceEventHooks EventHooks => _EventHooks;

        // - Props -

        // Login
        public bool   LoggedIn         => VivoxService.Instance?.IsLoggedIn ?? false;
        public string LoggedInPLayerID => VivoxService.Instance?.SignedInPlayerId ?? "";
        public bool   LoginInProgress { get; private set; } = false;

        // Join
        public bool HasJoinedAnyChannel { get { return (VivoxService.Instance?.ActiveChannels != null && VivoxService.Instance.ActiveChannels.Count > 0); } }

        // Status
        public WatchedValue<string> Status { get; private set; } = new WatchedValue<string>("Not Started");

        // Devices
        public ReadOnlyCollection<VivoxInputDevice> AvailableInput { get { return _InitdVService?.AvailableInputDevices; } }
        public ReadOnlyCollection<VivoxOutputDevice> AvailableOutput { get { return _InitdVService?.AvailableOutputDevices; } }

        public VivoxInputDevice ActiveInput   { get { return _InitdVService?.ActiveInputDevice; } }
        public VivoxOutputDevice ActiveOutput { get { return _InitdVService?.ActiveOutputDevice; } }

        public IVivoxService _InitdVService   { get { return (VivoxService.Instance != null && _Initd) ? VivoxService.Instance : null;} }

        // - Internal Members -
        private bool _Initd = false;
        private bool _InitStarted = false;
        private _VivoxServiceEventHooks _EventHooks = new _VivoxServiceEventHooks();

        private int _NumLoginAttempts = 0;
        private bool _CancelLoginAttempts = false;

        static private LogChannelShim Log => PLI.Logging.Core.Channels.Vivox;

        // -----------

        #region 'Ready' Status (static) + Gate (core, wrapper/usage)
        static public BoolAndReason VivoxIsReady()
        {
            if (Services.Vivox == null)         { return "Vivox disabled"; }
            if (!Services.Vivox.IsInitialized)  { return "Vivox not ready"; }
            if (Services.Vivox.LoginInProgress) { return "Vivox logging in"; }
            if (!Services.Vivox.LoggedIn)       { return "Vivox not logged in"; }

            return BoolAndReason.Success;
        }
        static public async Task<(BoolAndReason, bool)> VivoxIsReadyGate(float loginTimeoutInSecs, bool allowNoService, Func<BoolAndReason> then = null)
        {
            (BoolAndReason, bool) _ret(BoolAndReason reason, bool potentialHardFail = false)
            {
                bool isHardFail = !reason.State && potentialHardFail;

                // Ignore hard failures?
                bool ignoreHardFail = false;
                #if UNITY_EDITOR
                    ignoreHardFail = (EditorPrefs.GetBool("PLIEd_IgnoreVivoxHardFail", false));
                #endif

                if (isHardFail && ignoreHardFail)
                {
                    isHardFail = false;
                    Kingdoms.Dialogs.Toast("Ignoring Vivox Failure", reason.FailureReason);
                    reason = BoolAndReason.Success;
                }

                // on general success, call the proceed callback and use it's result
                if (reason.State && then != null)
                    { reason = then(); }

                return (reason, isHardFail);
            }

            // allow no service if vivox not enabled
            if(!GameConfig.UnityServices.IsVivoxEnabled)
                { allowNoService = true; }

            // No Service?
            if (Services.Vivox == null)
            {
                return _ret((allowNoService) ? BoolAndReason.Success : "Vivox Disabled", true); // true = hard fail
            }

            // Init not started?
            if(!Services.Vivox.InitStarted) { return _ret("Vivox failed to start", true); }

            // Wait for service init as needed
            if(!Services.Vivox.IsInitialized)
            {
                var loadingOverlay = Kingdoms.UIMgr.LoadingOverlay;
                loadingOverlay?.SetStatus("Vivox Starting");
                loadingOverlay?.Show(LoadingOverlayGroup.EVisFlags.ShowStage | LoadingOverlayGroup.EVisFlags.HideCancel);

                var waitForServiceReadyRes = await Services.Vivox.WaitForServiceReady();
                loadingOverlay.Hide();
                if (!waitForServiceReadyRes.State)
                    { return _ret("Vivox failed to start", true); }
            }

            //return _ret("DEV : HARD FAIL TESTING", true);

            // -- assumes service initd by here --

            // Logged in? cool, ret
            if (Services.Vivox.LoggedIn) 
                { return _ret(BoolAndReason.Success); }

            // No login even in progress? Start a login
            if (!Services.Vivox.LoginInProgress)
            {
                Services.Vivox._TryLogin().FireAndForget("Vivox.TryLogin");
                await Task.Delay(50); // release tick
            }

            // Logging in? start wait spinner and call 'then' on success
            if (Services.Vivox.LoginInProgress)
            {
                Services.Vivox._WaitForLogin(loginTimeoutInSecs, 200, then).FireAndForget("Vivox_WaitForLogin");
                return _ret("Vivox login in progress. Please Wait"); 
            }

            // Last chance. Logged in or fail
            if(Services.Vivox.LoggedIn) 
                { return _ret(BoolAndReason.Success); }

            return _ret("Failed to login to Vivox", true); 
        }
        static public async Task<BoolAndReason> FrontEndVivoxGate(string taskStatus, StatusMsgGroup statusMsg, Func<BoolAndReason> taskFunc, bool allowNoService = false)
        {
            const float VivoxLoginTimeout = 5.0f;
            if (taskFunc == null) { return "No task func passed"; }

            var loadingOverlay = Kingdoms.UIMgr.LoadingOverlay;

            (BoolAndReason vivoxGateRes, bool hardFail) = await PLIService_Vivox.VivoxIsReadyGate(VivoxLoginTimeout, allowNoService, () =>
            {
                loadingOverlay?.SetStatus(taskStatus);
                loadingOverlay?.Show(LoadingOverlayGroup.EVisFlags.ShowStage | LoadingOverlayGroup.EVisFlags.HideCancel);

                // run 'through the gate' task
                var taskRes = taskFunc();

                Kingdoms.Dialogs.RemoveToast("VivoxGate");
                

                return taskRes;
            });

            if (!vivoxGateRes.State)
            {
                if (hardFail)
                {
                    Kingdoms.Dialogs.ErrToast("Voice System", vivoxGateRes.FailureReason, VivoxLoginTimeout, "VivoxGate");
                    if (statusMsg != null) 
                        { statusMsg.ErrorMsg = vivoxGateRes.FailureReason; }
                }
                else
                {
                    loadingOverlay?.SetStatus(vivoxGateRes.FailureReason);
                    loadingOverlay?.Show(LoadingOverlayGroup.EVisFlags.ShowStage | LoadingOverlayGroup.EVisFlags.HideCancel);
                }


            }

            return vivoxGateRes;
        }

        #endregion

        // ---------------

        #region Ctor
        public PLIService_Vivox()
        {
            _ResetInternal();
        }
        private void _ResetInternal()
        {
            _Initd = false;
            _InitStarted = false;
            LoginInProgress = false;
            _NumLoginAttempts = 0;
            _CancelLoginAttempts = false;
        }
        #endregion

        #region Init / Destroy
        public async Task<BoolAndReason> InitAsNeeded(bool force = false)
        {
            if (_Initd && !force)
                { return BoolAndReason.Success; }

            // Services running?
            if (!await UnityServicesRoot._InsistServicesStarted())
                { return "UServices not started"; }

            _ResetInternal();
            _InitStarted = true;

            try
            {
                _UnsubscribeFromVivoxEventCallbacks();

                await VivoxService.Instance.InitializeAsync();

                // flag as init'd
                _Initd = true;

                _SubscribeToVivoxEventCallbacks();

                Log.Info("Vivox Service Initd");
                Status.Value = "Initialized";

                return BoolAndReason.Success;
            }
            catch (Exception e)
            {
                return _HandleException("Failed to init Vivox Service", e);
            }
        }

        public async Task Destroy()
        {
            _Initd = false;

            await Disconnect();

            _UnsubscribeFromVivoxEventCallbacks();
            _ResetInternal();

            Status.Value = "Destroyed";
            Log.Info("Vivox Service Destroyed");
        }
        #endregion

        #region Event Subscribe / Shims
        private void _SubscribeToVivoxEventCallbacks()
        {
            _UnsubscribeFromVivoxEventCallbacks();
            if(VivoxService.Instance == null) { return; }

            try
            {
                VivoxService.Instance.LoggedIn  += _OnLoggedIn;
                VivoxService.Instance.LoggedOut += _OnLoggedOut;

                VivoxService.Instance.AvailableInputDevicesChanged  += _OnAvailableInputDevicesChanged;
                VivoxService.Instance.AvailableOutputDevicesChanged += _OnAvailableOutputDevicesChanged;

                VivoxService.Instance.ConnectionRecovering      += _OnConnectionRecovering;
                VivoxService.Instance.ConnectionRecovered       += _OnConnectionRecovered;
                VivoxService.Instance.ConnectionFailedToRecover += _OnConnectionFailedToRecover;

                VivoxService.Instance.ChannelJoined             += _OnChannelJoined;
                VivoxService.Instance.ChannelLeft               += _OnChannelLeft;

                VivoxService.Instance.ParticipantAddedToChannel     += _OnParticipantAddedToChannel;
                VivoxService.Instance.ParticipantRemovedFromChannel += _OnParticipantRemovedFromChannel;

                VivoxService.Instance.ChannelMessageReceived    += _OnChannelMessageReceived;
                VivoxService.Instance.ChannelMessageEdited      += _OnChannelMessageEdited;
                VivoxService.Instance.ChannelMessageDeleted     += _OnChannelMessageDeleted;

                VivoxService.Instance.DirectedMessageReceived   += _OnDirectedMessageReceived;
                VivoxService.Instance.DirectedMessageDeleted    += _OnDirectedMessageDeleted;
                VivoxService.Instance.DirectedMessageEdited     += _OnDirectedMessageEdited;

            }
            catch (Exception e)
            {
                Log.Error("Failed to subscribe to vivox service events! : {0}", new Args(e.Message));
            }
        }

        private void _UnsubscribeFromVivoxEventCallbacks()
        {
            if (VivoxService.Instance == null) { return; }
            try
            {
                VivoxService.Instance.LoggedIn  -= _OnLoggedIn;
                VivoxService.Instance.LoggedOut -= _OnLoggedOut;

                VivoxService.Instance.AvailableInputDevicesChanged  -= _OnAvailableInputDevicesChanged;
                VivoxService.Instance.AvailableOutputDevicesChanged -= _OnAvailableOutputDevicesChanged;

                VivoxService.Instance.ConnectionRecovering      -= _OnConnectionRecovering;
                VivoxService.Instance.ConnectionRecovered       -= _OnConnectionRecovered;
                VivoxService.Instance.ConnectionFailedToRecover -= _OnConnectionFailedToRecover;

                VivoxService.Instance.ChannelJoined             -= _OnChannelJoined;
                VivoxService.Instance.ChannelLeft               -= _OnChannelLeft;

                VivoxService.Instance.ParticipantAddedToChannel     -= _OnParticipantAddedToChannel;
                VivoxService.Instance.ParticipantRemovedFromChannel -= _OnParticipantRemovedFromChannel;

                VivoxService.Instance.ChannelMessageReceived    -= _OnChannelMessageReceived;
                VivoxService.Instance.ChannelMessageEdited      -= _OnChannelMessageEdited;
                VivoxService.Instance.ChannelMessageDeleted     -= _OnChannelMessageDeleted;

                VivoxService.Instance.DirectedMessageReceived   -= _OnDirectedMessageReceived;
                VivoxService.Instance.DirectedMessageDeleted    -= _OnDirectedMessageDeleted;
                VivoxService.Instance.DirectedMessageEdited     -= _OnDirectedMessageEdited;
            }
            catch (Exception e)
            {
                Log.Error("Failed to unsubscribe from vivox service events! : {0}", new Args(e.Message));
            }
        }

        #endregion

        #region Event Shims

        private void _LogEvent(string msg)
        {
            Log.Verbose($"Vivox Event : {msg}");
        }

        #region Log In/Out
        private void _OnLoggedIn()
        {
            EventHooks.LoggedIn?.Invoke();
            Log.Verbose("Vivox Event : Logged In");
        }
    
        private void _OnLoggedOut()
        {
            EventHooks.LoggedOut?.Invoke();
            Log.Verbose("Vivox Event : Logged In");
        }
        #endregion

        #region Available devices changed
        private void _OnAvailableInputDevicesChanged()
        {

            EventHooks.AvailableInputDevicesChanged?.Invoke();
            _LogEvent("AvailableInputDevicesChanged");
            Kingdoms.Dialogs.DevToast("Input Devices Updated", "", DevToastChannels.Vivox);
        }

        private void _OnAvailableOutputDevicesChanged()
        {

            EventHooks.AvailableOutputDevicesChanged?.Invoke();
            _LogEvent("AvailableOutputDevicesChanged");
        }
        #endregion

        #region Connection Recovery
        private void _OnConnectionRecovering()
        {

            EventHooks.ConnectionRecovering?.Invoke();
            _LogEvent("ConnectionRecovering");
        }

        private void _OnConnectionRecovered()
        {

            EventHooks.ConnectionRecovered?.Invoke();
            _LogEvent("ConnectionRecovered");
        }

        private void _OnConnectionFailedToRecover()
        {

            EventHooks.ConnectionFailedToRecover?.Invoke();
            _LogEvent("ConnectionFailedToRecover");
        }
        #endregion

        #region Join / Left Channel
        private void _OnChannelJoined(string channel)
        {

            EventHooks.ChannelJoined?.Invoke(channel);
            _LogEvent($"ChannelJoined '{channel}'");
        }

        private void _OnChannelLeft(string channel)
        {

            EventHooks.ChannelLeft?.Invoke(channel);
            _LogEvent($"ChannelLeft '{channel}'");
        }
        #endregion

        #region Participant Added / Removed locally seen channel
        private void _OnParticipantAddedToChannel(VivoxParticipant participant)
        {

            EventHooks.ParticipantAddedToChannel?.Invoke(participant);
            _LogEvent($"ParticipantAddedToChannel '{participant.DisplayName}'");
        }
        private void _OnParticipantRemovedFromChannel(VivoxParticipant participant)
        {

            EventHooks.ParticipantRemovedFromChannel?.Invoke(participant);
            _LogEvent($"ParticipantRemovedFromChannel '{participant.DisplayName}'");
        }
        #endregion

        #region Channel message
        private void _OnChannelMessageReceived(VivoxMessage msg)
        {

            EventHooks.ChannelMessageReceived?.Invoke(msg);
            _LogEvent($"ChannelMessageReceived '{msg.MessageId}'");
        }
        private void _OnChannelMessageEdited(VivoxMessage msg)
        {

            EventHooks.ChannelMessageEdited?.Invoke(msg);
            _LogEvent($"ChannelMessageEdited '{msg.MessageId}'");
        }
        private void _OnChannelMessageDeleted(VivoxMessage msg)
        {

            EventHooks.ChannelMessageDeleted?.Invoke(msg);
            _LogEvent($"ChannelMessageDeleted '{msg.MessageId}'");
        }
        #endregion

        #region Direct message
        private void _OnDirectedMessageReceived(VivoxMessage msg)
        {

            EventHooks.DirectedMessageReceived?.Invoke(msg);
            _LogEvent($"DirectedMessageReceived '{msg.MessageId}'");
        }

        private void _OnDirectedMessageDeleted(VivoxMessage msg)
        {

            EventHooks.DirectedMessageDeleted?.Invoke(msg);
            _LogEvent($"DirectedMessageDeleted '{msg.MessageId}'");
        }

        private void _OnDirectedMessageEdited(VivoxMessage msg)
        {

            EventHooks.DirectedMessageEdited?.Invoke(msg);
            _LogEvent($"DirectedMessageEdited '{msg.MessageId}'");
        }
        #endregion

        #endregion

        // ------------

        #region Sign In change Handlers
        public async void OnSignedIn()
        {
            await Task.Delay(200); // release tick

            var waitForServiceReadyRes = await WaitForServiceReady();
            if(!waitForServiceReadyRes.State)
            {
                Kingdoms.Dialogs.ErrToast("Vivox Failure", "Failed to start");
                return;
            }

            await Task.Delay(100); // release tick

            await _TryLogin();
        }

        public void OnSigningOut()
        {
            
        }

        public async void OnSignedOut()
        {
            await LogOut();
        }
        #endregion

        // ------------

        #region Login (start / stop attempts, try login, wait spinner)
        public async void StartLoginAttempts(Action<BoolAndReason> onSuccessOrGaveUp, int maxAttempts = kDefaultMaxAttempts)
        {
            if(!InsistServiceIsReady()) { return; }

            if (LoggedIn)
            {
                Log.Warn("Vivox already logged in?");
                onSuccessOrGaveUp?.Invoke(BoolAndReason.Success); // done as success
                return;
            }

            // Reset
            _NumLoginAttempts = 0;


            bool keepGoing = !LoggedIn && _NumLoginAttempts < maxAttempts;
            bool cancelled = _CancelLoginAttempts || GameCore_GameObj.AppKillRequested;

            while (keepGoing && !cancelled)
            {
                var loginRes = await _TryLogin();
                if(loginRes.State) { break; }

                Log.Warn($"Vivox login attempt ({_NumLoginAttempts}) failed : {loginRes.FailureReason}");

                await Task.Delay(Mathf.CeilToInt(kRetryDelayInSecs * 1000f));
            }

            if (!LoggedIn)
            {
                string gaveUpMsg = "Gave up on logging into vivox";
                onSuccessOrGaveUp?.Invoke(gaveUpMsg);
                return;
            }

            onSuccessOrGaveUp?.Invoke(BoolAndReason.Success);
                        
        }
        public void StopLoginAttempts() { _CancelLoginAttempts = true; }

        public async Task<BoolAndReason> _TryLogin()
        {
            BoolAndReason _fail(string msg, bool logAsErr = false)
            {
                string failMsg = $"Vivox Login Fail : {msg}";
                if (logAsErr) { Log.Error(failMsg); }
                Status.Value = failMsg;
                return msg;
            }

            if (!InsistServiceIsReady()) { return _fail("Service not ready"); }

            // Signed in?
            var signInRes = Auth.IsSignedIntoOnlineService();
            if (!signInRes.State)
                { return _fail(signInRes.FailureReason); }

            if (LoggedIn) { return BoolAndReason.Success; }

            // inc attempts, bail if in progress
            _NumLoginAttempts++;
            if (LoginInProgress) { return _fail("Login already in progress"); }

            // Options
            var loginOptions = new LoginOptions()
            {
                DisplayName = Auth.DisplayName,
                EnableTTS = true,
                //BlockedUserList = TODO : Pull from friends service
                ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.FivePerSecond,
            };

            // Login
            LoginInProgress = true;
                try
                {
                    await VivoxService.Instance.LoginAsync(loginOptions);
                    Log.Info("Vivox Logged In");
                    Status.Value = "Logged in";
                    Kingdoms.Dialogs.DevToast("Logged into Vivox", "", DevToastChannels.Vivox);
                }
                catch (Exception ex)
                {
                    LoginInProgress = false;
                    return _fail($"Failed to login to Vivox : {ex.Message}", true);
                }
            LoginInProgress = false;

            if(VivoxService.Instance.IsLoggedIn) { _PostLogin(); }
            return (VivoxService.Instance.IsLoggedIn) ? BoolAndReason.Success : "Failed to login to Vivox...";
        }

        private async void _PostLogin()
        {
            if(!VivoxService.Instance.IsLoggedIn) { return; }

            // grab inp, output device from options and set
            var inpDeviceID = GameOptions.Cur.Audio.VivoxInpDeviceID.Value;
            if(!string.IsNullOrEmpty(inpDeviceID))
            {
                await Services.Vivox.SelectInputDeviceByID(inpDeviceID);
            }
            var outDeviceID = GameOptions.Cur.Audio.VivoxOutDeviceID.Value;
            if (!string.IsNullOrEmpty(outDeviceID))
            {
                await Services.Vivox.SelectOutputDeviceByID(outDeviceID);
            }

            // now that we've attempted to select the device from options, let the actual system state take over
            GameOptions.Cur.Audio.VivoxInpDeviceID.Context.SystemPollingEnabled = true;
            GameOptions.Cur.Audio.VivoxOutDeviceID.Context.SystemPollingEnabled = true;

            // Call post login event
            Services.Vivox.EventHooks.PostLogin?.Invoke();
        }

        public async Task<BoolAndReason> _WaitForLogin(float timeoutInSecs, int pollDelayInMillis = 200, Func<BoolAndReason> onSuccess = null)
        {
            double startWaitTS = Clocks.FromEpoch.InSecs();
            float waitDurInSecs = 0.0f;
            while(!LoggedIn && (waitDurInSecs < timeoutInSecs))
            {
                if(GameCore_GameObj.AppKillRequested) { return BoolAndReason.Success; }

                await Task.Delay(pollDelayInMillis);
                waitDurInSecs = (float)Clocks.Dur.FromEpoch.InSecs(startWaitTS);
            }

            if(LoggedIn) 
            { 
                var thenRes = onSuccess?.Invoke(); 
                if(!thenRes.State) { return thenRes; }
            }

            return LoggedIn ? BoolAndReason.Success : $"Timed out waiting for Vivox login after ({waitDurInSecs:F2}) sec.";
        }

        #endregion

        #region LogOut / Disconnect

        public async Task<BoolAndReason> LogOut()
        {
            StopLoginAttempts();

            if (VivoxService.Instance == null) { return "Service not ready"; }
            if (!LoggedIn)               { return BoolAndReason.Success; }

            await LeaveAllChannels();

            bool isEditor = Application.isEditor;
            bool isAppKill = GameCore_GameObj.AppKillRequested;

            #if UNITY_EDITOR
                isEditor = true;
            #endif

            BoolAndReason skipReason = BoolAndReason.Success;

            // Skip this call in the editor? See : https://forum.unity.com/threads/unity-editor-crashes-when-leaving-play-mode.1548665/
            //if (isEditor) { skipReason = "Skip in editor"; }
            //else if (isAppKill) { skipReason = Kingdom_Staging.kAppKill; }

            // Skip?
            if (!skipReason.State)
            {
                Log.Verbose($"Vivox : LogOut skipped : {skipReason.FailureReason}");
                return "Skipped";
            }

            try
            {
                Log.Verbose("Vivox : Logging out");
                await VivoxService.Instance.LogoutAsync();
                Status.Value = "Logged out";
                Log.Verbose("Vivox : Logged out");

                return BoolAndReason.Success;
            }
            catch (Exception ex)
            {
                return _HandleException("Failed to log out of Vivox", ex);
            }
        }

        public async Task Disconnect()
        {
            Status.Value = "Disconnecting";

            var logoutRes = await LogOut();

            _ResetInternal();

            Status.Value = "Disconnected";
        }
        #endregion

        //-----------------------

        #region Get Full channel key (defuncting exp, deprecation or 'get it functional' candidate)
        public string GetFullChannelKey(string channelKey)
        {
            if (channelKey == kEchoChannel) { return kEchoChannel; }

            // if #prefixed.. otherwise as is
            return channelKey;
        }
        #endregion

        #region Channels Join, Leave, Leave all, is in
        public async Task<BoolAndReason> JoinChannel(string channelKey, bool makeActive, ChatCapability chatCaps, Channel3DProperties _3DProps = null)
        {
            // TODO : switch to pending joins style (maybe w/ result?, needs a less stally style)

            if (!InsistServiceIsReady()) { return "Service not ready"; }
            if(string.IsNullOrEmpty(channelKey)) { return "Passed empty channel key"; }

            // Wait for login option
            //await _WaitForLogin(Settings_WaitForLoginTimeoutInSecs);

            // Not logged in? bail
            if (!LoggedIn)
                { return "Not logged into Vivox"; }

            string fullChannelKey = GetFullChannelKey(channelKey);

            // already in channel? ret success
            if (VivoxService.Instance.ActiveChannels.ContainsKey(fullChannelKey))
                { return BoolAndReason.Success; }

            // Join
            ChannelOptions opts = new ChannelOptions { MakeActiveChannelUponJoining = makeActive };
            try
            {
                // 3D / Positional
                if (_3DProps != null)
                {
                    await VivoxService.Instance.JoinPositionalChannelAsync(fullChannelKey, chatCaps, _3DProps, opts);
                }
                // Echo
                else if (fullChannelKey == kEchoChannel)
                {
                    await VivoxService.Instance.JoinEchoChannelAsync(fullChannelKey, chatCaps, opts);
                }
                // 2D / Group
                else
                {
                    await VivoxService.Instance.JoinGroupChannelAsync(fullChannelKey, chatCaps, opts);
                }
            }
            catch (Exception ex)
            {
                return _HandleException($"Failed to join channel '{fullChannelKey}'", ex);
            }

            Kingdoms.Dialogs.DevToast("Vivox", $"Joined {(_3DProps != null ? "(3D)":"(2D)")} channel\n{channelKey} ", DevToastChannels.Vivox);
            Status.Value = $"Joined '{channelKey}'";

            return BoolAndReason.Success;
        }

        public async Task<BoolAndReason> LeaveChannel(string channelKey)
        {
            if (!InsistServiceIsReady())           { return "Service not ready"; }
            if (string.IsNullOrEmpty(channelKey))  { return "Passed empty channel key"; }
            if(!LoggedIn)                          { return "Not logged in"; }

            string fullChannelKey = GetFullChannelKey(channelKey);

            // not in channel? ret success
            if (!VivoxService.Instance.ActiveChannels.ContainsKey(fullChannelKey))
                { return BoolAndReason.Success; }

            try
            {
                await VivoxService.Instance.LeaveChannelAsync(fullChannelKey);
            }
            catch (Exception ex)
            {
                return _HandleException($"Failed to Leave Vivox Channel '{fullChannelKey}'", ex);
            }

            Status.Value = $"Left '{channelKey}'";
            return BoolAndReason.Success;
        }

        public async Task<BoolAndReason> LeaveAllChannels()
        {
            if(!InsistServiceIsReady(true)) { return "Service not ready"; }
            if(!HasJoinedAnyChannel)        { return BoolAndReason.Success; }

            bool isEditor = Application.isEditor;
            bool isAppKill = GameCore_GameObj.AppKillRequested;

            #if UNITY_EDITOR
                isEditor = true;
            #endif

            BoolAndReason skipReason = BoolAndReason.Success;

            // Skip this call in the editor? See : https://forum.unity.com/threads/unity-editor-crashes-when-leaving-play-mode.1548665/
            if (isEditor) { skipReason = "Skip in editor"; }
            else if (isAppKill) { skipReason = Kingdom_Staging.kAppKill; }

            // Skip?
            if (!skipReason.State)
            {
                Log.Verbose($"Vivox : Leave all Channels skipped : {skipReason.FailureReason}");
                return "Skipped";
            }

            try
            {
                Log.Verbose("Vivox : Leaving all Channels");
                await VivoxService.Instance.LeaveAllChannelsAsync();
                Status.Value = "Left all channels";
                Log.Verbose("Vivox : Left all Channels");
                return BoolAndReason.Success;
            }
            catch (Exception ex)
            {
                return _HandleException("Failed to leave all Vivox channels", ex);
            }

        }

        public bool IsInChannel(string channelKey)
        {
            if(!InsistServiceIsReady()) { return false; }
            if(string.IsNullOrEmpty(channelKey)) { return false; }

            string fullChannelKey = GetFullChannelKey(channelKey);
            return VivoxService.Instance.ActiveChannels.ContainsKey(fullChannelKey);
        }
        #endregion


        #region Echo / Test 
        public async Task<BoolAndReason> SwitchToEcho()
        {
            var leaveRes = await LeaveAllChannels();
            return await JoinChannel(kEchoChannel, true, ChatCapability.AudioOnly);
        }
        #endregion

        //-----------------------
        #region Mute/Unmute input device
        public void MuteInputDevice()
        {
            _InitdVService?.MuteInputDevice();
        }
        public void UnmuteInputDevice()
        {
            _InitdVService?.UnmuteInputDevice();
        }
        public bool InputDeviceIsMuted()
        {
            return _InitdVService?.IsInputDeviceMuted ?? GameConfig.Audio.DefaultMicMutedState;
        }
        #endregion

        //-----------------------

        #region Device helpers (get from list by name, id, Device Name <-> Device ID)
        // assumes the lists are likely to be short, so not worth the overhead of managing a dict/map
        public BoolAndReason TryGetDeviceByName<DeviceT>(IReadOnlyList<DeviceT> devices, string deviceName, out DeviceT device) where DeviceT : IVivoxAudioDevice
        {
            device = default;
            if (string.IsNullOrEmpty(deviceName))       { return "No device name passed"; }
            if (devices == null || devices.Count == 0)  { return "No devices passed";     }
            foreach (var cur in devices)
            {
                if (cur.DeviceName == deviceName) 
                    { device = cur; return BoolAndReason.Success; }
            }

            return $"Failed to find device '{deviceName}'";
        }
        public BoolAndReason TryGetDeviceByID<DeviceT>(IReadOnlyList<DeviceT> devices, string deviceID, out DeviceT device) where DeviceT : IVivoxAudioDevice
        {
            device = default;
            if (string.IsNullOrEmpty(deviceID))         { return "No device ID passed"; }
            if (devices == null || devices.Count == 0)  { return "No devices passed";   }
            foreach (var cur in devices)
            {
                if (cur.DeviceID == deviceID)
                    { device = cur; return BoolAndReason.Success; }
            }

            return $"Failed to find device ID '{deviceID}'";
        }

        public string GetDeviceNameForID<DeviceT>(IReadOnlyList<DeviceT> devices, string deviceID) where DeviceT : IVivoxAudioDevice
        {
            if (string.IsNullOrEmpty(deviceID))         { return ""; }
            if (devices == null || devices.Count == 0)  { return ""; }
            foreach (var cur in devices)
            {
                if (cur.DeviceID == deviceID)
                    { return cur.DeviceName; }
            }
            return "";
        }
        public string GetDeviceIDForName<DeviceT>(IReadOnlyList<DeviceT> devices, string deviceName) where DeviceT : IVivoxAudioDevice
        {
            if (string.IsNullOrEmpty(deviceName))      { return ""; }
            if (devices == null || devices.Count == 0) { return ""; }
            foreach (var cur in devices)
            {
                if (cur.DeviceName == deviceName)
                { return cur.DeviceID; }
            }
            return "";
        }

        static public List<string> GetDeviceIDs<DeviceT>(IReadOnlyList<DeviceT> devices) where DeviceT : IVivoxAudioDevice
        {
            List<string> res = new List<string>();
            if (devices == null || devices.Count == 0) { return res; }
            foreach (var cur in devices)
            {
                res.Add(cur.DeviceID); 
            }
            return res;
        }
        static public List<string> GetDeviceNames<DeviceT>(IReadOnlyList<DeviceT> devices) where DeviceT : IVivoxAudioDevice
        {
            List<string> res = new List<string>();
            if (devices == null || devices.Count == 0) { return res; }
            foreach (var cur in devices)
            {
                res.Add(cur.DeviceName);
            }
            return res;
        }

        #endregion

        #region Devices (select by obj, name, id) 

        // Inp/Out by device obj

        [MethodImpl(MethodImplOptions.AggressiveInlining)] // does this do anything w/ returning async calls? eh, intent marker i guess. TODO : eval - JKB
        public async Task<BoolAndReason> SelectInputDevice(VivoxInputDevice inpDevice)
        {
            if(inpDevice == null)             { return "No input device passed"; }
            if(VivoxService.Instance == null) { return "No Service"; }

            try
            {
                await VivoxService.Instance.SetActiveInputDeviceAsync(inpDevice);
            }
            catch (Exception ex) 
            {
                return _HandleException($"Failed setting active input device '{inpDevice?.DeviceName??"?"}'", ex);
            }
            return BoolAndReason.Success;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)] 
        public async Task<BoolAndReason> SelectOutputDevice(VivoxOutputDevice outDevice)
        {
            if(outDevice == null)             { return "No output device passed"; }
            if(VivoxService.Instance == null) { return "No Service"; }

            try
            {
                await VivoxService.Instance.SetActiveOutputDeviceAsync(outDevice);
            }
            catch (Exception ex)
            {
                return _HandleException($"Failed setting active output device '{outDevice?.DeviceName ?? "?"}'", ex);
            }
            return BoolAndReason.Success;
        }

        // Inp/Out by device name
        public async Task<BoolAndReason> SelectInputDeviceByName(string inpDeviceName)
        {
            var getRes = TryGetDeviceByName<VivoxInputDevice>(AvailableInput, inpDeviceName, out var inpDevice);
            if(!getRes.State) { return getRes; }

            return await SelectInputDevice(inpDevice);
        }
        public async Task<BoolAndReason> SelectOutputDeviceByName(string outDeviceName)
        {
            var getRes = TryGetDeviceByName<VivoxOutputDevice>(AvailableOutput, outDeviceName, out var outDevice);
            if (!getRes.State) { return getRes; }

            return await SelectOutputDevice(outDevice);
        }

        // Inp/Out by device ID
        public async Task<BoolAndReason> SelectInputDeviceByID(string inpDeviceID)
        {
            var getRes = TryGetDeviceByID<VivoxInputDevice>(AvailableInput, inpDeviceID, out var inpDevice);
            if (!getRes.State) { return getRes; }

            try
            {
                await VivoxService.Instance.SetActiveInputDeviceAsync(inpDevice);
            }
            catch (Exception ex)
            {
                return _HandleException($"Failed to select input device", ex);
            }

            return BoolAndReason.Success;

        }
        public async Task<BoolAndReason> SelectOutputDeviceByID(string outDeviceID)
        {
            var getRes = TryGetDeviceByID<VivoxOutputDevice>(AvailableOutput, outDeviceID, out var outDevice);
            if (!getRes.State) { return getRes; }

            return await SelectOutputDevice(outDevice);
        }
        #endregion

        #region Dev next
        public void _DevSelectNextInputDevice()
        {
            if (VivoxService.Instance == null) { return; }

            var inpDevices = VivoxService.Instance.AvailableInputDevices;
            if(inpDevices.Count == 0) { return; }

            var activeDevice = VivoxService.Instance.ActiveInputDevice;
            if (activeDevice == null) { VivoxService.Instance.SetActiveInputDeviceAsync(inpDevices[0]); return; } // select first

            int activeIdx = -1;

            for(int i = 0; i< inpDevices.Count; i++)
            {
                if (inpDevices[i].DeviceID == activeDevice.DeviceID) { activeIdx  = i; break; }
            }
            int nextIdx = (activeIdx == -1) ? 0 : ((activeIdx + 1) % inpDevices.Count);

            VivoxService.Instance.SetActiveInputDeviceAsync(inpDevices[nextIdx]);
        }
        #endregion

        #region Get Available inp / out device IDs (static, any entry)
        static public BoolAndReason _TryGetAvailableInputDeviceIDs(out List<string> deviceIDs)
        {
            deviceIDs = new List<string>(); // reset out

            var isReady = PLIService_Vivox.VivoxIsReady();
            if (!isReady.State) { return isReady; }

            deviceIDs = GetDeviceIDs(Services.Vivox?.AvailableInput);
            return (deviceIDs.Count == 0) ? "No devices found" : BoolAndReason.Success;
        }

        static public BoolAndReason _TryGetAvailableOutputDeviceIDs(out List<string> deviceIDs)
        {
            deviceIDs = new List<string>(); // reset out

            var isReady = PLIService_Vivox.VivoxIsReady();
            if (!isReady.State) { return isReady; }

            deviceIDs = GetDeviceIDs(Services.Vivox?.AvailableOutput);
            return (deviceIDs.Count == 0) ? "No devices found" : BoolAndReason.Success;
        }

        #endregion
        
        //-----------------------

        #region Internal Util : (log/errs/insists)
        public bool InsistServiceIsReady(bool skipLog = false)
        {
            if (VivoxService.Instance == null)
            {
                if (!skipLog)
                    { Log.Error($"Vivox Service Instance is null"); }
                return false;
            }
            if (_Initd == false)
            {
                if(!skipLog)
                    { Log.Error($"Vivox Service not initialized"); }
                return false;
            }

            return true;
        }
        public async Task<BoolAndReason> WaitForServiceReady(float timeoutInSecs = GameConfig.UnityServices.WaitForVivoxServiceInitTimeoutInSecs, int pollDelayInMillis = 200)
        {
            double startWaitTS = Clocks.FromEpoch.InSecs();
            float waitDurInSecs = 0.0f;

            bool ready = InsistServiceIsReady(true); // true = skip logging on failure
            while (!ready && (waitDurInSecs < timeoutInSecs))
            {
                if (GameCore_GameObj.AppKillRequested) { return BoolAndReason.Success; }

                await Task.Delay(pollDelayInMillis);
                ready = InsistServiceIsReady(true);
                waitDurInSecs = (float)Clocks.Dur.FromEpoch.InSecs(startWaitTS);
            }

            return ready ? BoolAndReason.Success : $"Timed out waiting for Vivox start after ({waitDurInSecs:F2}) sec.";
        }

        public BoolAndReason _HandleException(string msg, Exception e)
        {
            string failMsg = $"{msg} : {e.Message}";
            Status.Value = $"[Exception] {failMsg}";
            Log.Error(failMsg);
            return failMsg;
        }

        #endregion
    }

    public class VivoxController : MonoBehaviour
    {
        public int AudibleDistance = 50;
        public int ConversationalDistance = 5;
        public GameObject VivoxParticipantPrefab;

        // Settings
        public const float kRetryDelayInSecs   = 5.0f;
        public const int   kDefaultMaxAttempts = (3 * 60) / (int)kRetryDelayInSecs; // 3 mins of attempts

        // Lobby controller
        public LobbyController LobbyController => (_UseLobbyController ? Kingdoms.Lobby._Context.Controller : null);

        public void _LinkLobbyController()   { _UseLobbyController = true;  }
        public void _UnlinkLobbyController() { _UseLobbyController = false; }
        private bool _UseLobbyController = false;


        private static VivoxController s_Singleton;
        public static VivoxController Instance => s_Singleton;

        // Props
        public WatchedValue<string> Status { get; private set; } = new WatchedValue<string>("");

        public bool   LoggedIn        => VivoxService.Instance?.IsLoggedIn ?? false;
        public bool   HasJoined       { get { return (VivoxService.Instance?.ActiveChannels != null && VivoxService.Instance.ActiveChannels.Count > 0 ); } }
        public string CurLobbyChannel { get { return LobbyController?.LocalLobby?.LobbyID.Value ?? ""; } }
        public string myPlayerID;
        private VivoxParticipant participant_self = null;
        public float LocalVoiceSoundLevel = 0.0f;

        // -----------------

        public Dictionary<string, GameObject> VivoxTaps = null;

        // -----------------

        private Dictionary<string, Task> m_PendingJoins = new();

        private LogChannelShim Log => PLI.Logging.Core.Channels.Vivox;

        // -----------------

        public Channel3DProperties GetPositionalChannel3DProps()
        {
            return new Channel3DProperties(AudibleDistance, ConversationalDistance, 1, AudioFadeModel.ExponentialByDistance);
        }

        #region Start, Init
        private async void Start()
        {
            s_Singleton = this;
            VivoxTaps = new Dictionary<string, GameObject>();

            _BindToServiceEvents();

            #region Wait for service & init (deprecating, but log still handy atm)
            // Wait for service to be set
            int tries = 20 * 10; // ~ 10 sec @ 100ms
            while(!(Services.Vivox != null && Services.Vivox.IsInitialized))
            {
                await Task.Delay(100);
                tries--;
                if(tries <= 0) { break; }
            }

            if(Services.Vivox == null || !Services.Vivox.IsInitialized) 
            {
                Log.Error("Vixox service obj not created or init'd. UnityServices_GameObj failed?");
                return; 
            }
            #endregion

        }
        #endregion

        // ---------------------

        #region Bind/Unbind Service events
        private void _BindToServiceEvents()
        {
            if(Services.Vivox == null) { return; }
            _UnBindFromServiceEvents();

            //var evtObj = VivoxService.Instance;
            var evtObj = Services.Vivox.EventHooks;

            evtObj.PostLogin += OnPostLogin;
            evtObj.LoggedOut+= OnLoggedOut;
            evtObj.ChannelMessageReceived += OnChannelMessageReceived;

            evtObj.ChannelJoined += _OnChannelJoined;
            evtObj.ChannelLeft   += _OnChannelLeft;

            evtObj.ParticipantAddedToChannel     += _OnParticipantAdded;
            evtObj.ParticipantRemovedFromChannel += _OnParticipantRemoved;
        }
        private void _UnBindFromServiceEvents()
        {
            if (Services.Vivox == null) { return; }

            //var evtObj = VivoxService.Instance;
            var evtObj = Services.Vivox.EventHooks;

            evtObj.ChannelMessageReceived -= OnChannelMessageReceived;

            evtObj.ChannelJoined -= _OnChannelJoined;
            evtObj.ChannelLeft   -= _OnChannelLeft;

            evtObj.ParticipantAddedToChannel     -= _OnParticipantAdded;
            evtObj.ParticipantRemovedFromChannel -= _OnParticipantRemoved;
        }
        #endregion

        #region Event Handlers (Login/Logout, Join/Leave channel, Participant added/removed from channel)
        private void OnPostLogin()
        {
            #pragma warning disable CS0162 
            if (GameConfig.Audio.DefaultMicMutedState == true)
                Services.Vivox?.MuteInputDevice();

            #pragma warning restore CS0162 
        }
        private void OnLoggedOut()
        {

        }
        private void _OnChannelJoined(string channelName)
        {
            Log.Info($"Vivox joined channel [{channelName}]");
            DisableOutputDevice(); 
        }
        private void _OnChannelLeft(string channelName)
        {
            Log.Info($"Vivox left channel [{channelName}]");
        }
        private void _OnParticipantAdded(VivoxParticipant participant)
        {
            if (participant == null)
            {
                Log.Warn("Null participant");
                return;
            }
            if (!participant.IsSelf)
            {
                Log.Info($"Participant added to channel: {participant.PlayerId}");
                CreateParticipantTap(participant);
            }
            else
            {
                Log.Info($"Participant(SELF) added to channel: {participant.PlayerId}");
                myPlayerID = participant.PlayerId;
                participant_self = participant;
            }

        }
        private void _OnParticipantRemoved(VivoxParticipant participant)
        {
            Log.Info($"Participant removed from channel: {participant.PlayerId}");
            DestroyParticipantTap(participant);
        }
        #endregion

        // ---------------------

        #region Internal Util : Is Valid Participant?
        private bool IsValidParticipant(VivoxParticipant participant)
        {
            // ug, ugly way to do this... TODO : add a prop to the vivox code to check if m_ParentParticipant is null. 
            if(participant == null) { return false; }
            try
            {
                var test = participant.IsSelf;
            }
            catch { return false; }

            return true;
        }
        #endregion

        private void Update()
        {
            if(Services.Vivox == null || !Services.Vivox.LoggedIn) { return; }

            if (IsValidParticipant(participant_self) && Kingdoms.InGame.IsActive)
            {
                double audioEnergy = participant_self.AudioEnergy;
                // audioEnergy comes in at a range of .4-.7 where .5 is whisper and .7 is yelling
                float soundStat = (float)(Math.Clamp(45 * audioEnergy - 21.5, 0, 10));
                if (soundStat > 0)
                {
                    LocalVoiceSoundLevel = soundStat;
                    //UpdateSoundStat(soundStat);  HOW IT SHOULD WORK?
                }
                else
                {
                    LocalVoiceSoundLevel = 0;
                }

            }
        }
        private void UpdateSoundStat(float soundStat)
        {
            if (!Kingdoms.InGame.IsActive || Local.Player == null || Local.Player.ControlledCharacter == null) { return; } 
            
            if (Local.Player.ControlledCharacter.TryGetComponent(out CharacterStatus status))
            {
                if (status && status.HasStat(Stat.Sound))
                {
                    CharacterStat sound = status.GetStat(Stat.Sound);
                    if (soundStat > sound.Current.Value)
                    {
                        float difference = soundStat - sound.Current.Value;
                        // Adjust the stat by only the difference amount
                        CharacterStatus.Adjust(ref sound.Current, difference);
                        status.SetStat(Stat.Sound, sound);
                    }
                }
            }
            else
            {
                Log.Error("Sound stat not found.");
            }
        }

        #region Destroy
        private void OnDestroy()
        {
            VivoxTaps?.Clear();
            VivoxTaps = null;
            m_PendingJoins.Clear();
            m_PendingJoins = null;
        }

        #endregion

        #region Join Lobby Channel + Participant Taps
        public async Task<BoolAndReason> TryJoinLobbyChannel()
        {
            // No lobby controller or channel? bail
            if (LobbyController == null)                 { return "No Lobby Controller"; }
            if (string.IsNullOrEmpty(CurLobbyChannel))   { return "Lobby Controller has no Local Lobby"; }

            return await Services.Vivox.JoinChannel(CurLobbyChannel, true, ChatCapability.TextAndAudio, null);
        }
        #endregion

        #region Create/Destroy/Get Participant Taps

        public const string kTapObjPrefix = "VPT_";

        public void CreateParticipantTap(VivoxParticipant participant)
        {
            if (participant == null || participant.IsSelf) { return; };
            //Log.Info($"Creating participant tap for [${participant.DisplayName}]");
            participant.SetLocalVolume(0);
            var vParticipant = Instantiate(VivoxParticipantPrefab, transform);
            var tap = vParticipant.GetComponent<VivoxParticipantTap>();
            tap.ChannelName = CurLobbyChannel;
            tap.ParticipantName = participant.PlayerId;

            tap.enabled = true; // Flip the switch! (disabled in prefab so it doesn't try to register and fail on enable/instantiate)

            // TODO: Switch displayname for playerID maybe?

            vParticipant.name = $"{kTapObjPrefix}_{CurLobbyChannel}_{participant.DisplayName}";
            vParticipant.transform.GetChild(0).GetComponent<AudioSource>().mute = true;

            //VivoxTaps.Add(participant.PlayerId, vParticipant); // tap w/ player id may exist in dict, if so add(...) will throw exception! (Cory's log from 11/13/2024 confirms this)
            VivoxTaps[participant.PlayerId] = vParticipant;

            string msgA = $"Created Tap '{participant.DisplayName}'";
            string msgB = $"Channel '{tap.ChannelName}'";
            Kingdoms.Dialogs.DevToast(msgA, msgB, DevToastChannels.Vivox);
            Log.Verbose($"{msgA} {msgB}");
        }

        public void DestroyParticipantTap(VivoxParticipant participant)
        {
            if (participant == null || participant.IsSelf) { return; };
            //Log.Info($"Destroying participant tap for [${participant.DisplayName}]");
            
            var taps = _GetChildVTapComponents();
            foreach (var curTap in taps)
            {
                // Gonna skip the lobby channel check & early out return. Lets just kill all taps for this participant - JKB
                //if (cur.gameObject.name.Contains(CurLobbyChannel)) ..

                if (curTap.ParticipantName == participant.PlayerId)
                {
                    VivoxTaps.Remove(participant.PlayerId);
                    Destroy(curTap.gameObject);
                    Log.Verbose($"Removed Vivox Tap for '{participant.DisplayName}'/'{participant.PlayerId}'");
                }
            }
        }

        private List<VivoxParticipantTap> _GetChildVTapComponents(string containsTerm = "")
        {
            List<VivoxParticipantTap> res = new();

            for (var i = 0; i < transform.childCount; ++i)
            {
                var curChild = transform.GetChild(i);

                // no vtap prefix? skip
                if (!curChild.name.StartsWith(kTapObjPrefix)) { continue; }

                // skip if passed term and not in name
                if (!string.IsNullOrEmpty(containsTerm) && !curChild.name.Contains(containsTerm)) { continue; }

                // got a tap? err otherwise
                var tap = curChild.GetComponent<VivoxParticipantTap>();
                if (tap == null)
                {
                    Log.Error($"GameObject '{curChild.name}' starts with prefix '{kTapObjPrefix}' but no tap component found!");
                    continue;
                }

                res.Add(tap);
            }

            return res;
        }


        #endregion

        // -----------------

        #region Walkie Systems

        public void UnMuteWalkieUser(string playerID)
        {
            VivoxTaps.TryGetValue(playerID, out GameObject tapObject);
            if (tapObject == null)
            {
                Log.Warn($"{playerID} not found in participant dictionary");
                return;
            }
            tapObject.transform.GetChild(0).TryGetComponent<AudioSource>(out AudioSource walkieSource);
            if (walkieSource != null)
            {
                walkieSource.mute = false;
            }
        }

        public void MuteWalkieUser(string playerID)
        {
            VivoxTaps.TryGetValue(playerID, out GameObject tapObject);
            if (tapObject == null)
            {
                Log.Warn($"{playerID} not found in participant dictionary");
                return;
            }
            tapObject.transform.GetChild(0).TryGetComponent<AudioSource>(out AudioSource walkieSource);
            if (walkieSource != null)
            {
                walkieSource.mute = true;
            }
        }
        #endregion

        #region Text
        private void OnChannelMessageReceived(VivoxMessage message)
        {
            string messageText = message.MessageText;
            try
            {
                VivoxTextUiHandler.Instance.PostMessage(message);
            }
            catch (Exception ex)
            {
                Services.Vivox._HandleException($"Failed to post text message", ex);
                return;
            }
            Log.Info($"Vivox message received: [${messageText}]");
        }

        public async void SendMessageAsync(string message)
        {
            if (string.IsNullOrEmpty(message)) return;  // throw error for string
            if(!Services.Vivox.IsInChannel(CurLobbyChannel))
            {
                Log.Error("Cannot send text message. Not in Lobby channel");
                return;
            }

            try
            {
                await VivoxService.Instance.SendChannelTextMessageAsync(CurLobbyChannel, message);
            }
            catch (Exception ex)
            {
                Services.Vivox._HandleException($"Failed to send text message", ex);
                return;
            }

            Log.Info($"Vivox message sent: [${message}]");
        }

        #endregion

        // --------------------

        #region For Each / Get Player on Channel (channel + playerID -> participant / callback)
        public BoolAndReason ForEachPlayerOnChannel(string channelKey, Action<VivoxParticipant> action)
        {
            BoolAndReason _fail(string reason)
            {
                string failMsg = $"Failed to get Vivox player : {reason}";
                Log.Warn(failMsg);
                return failMsg;
            }

            if (LobbyController == null) { return _fail("No lobby controller"); }
            if (string.IsNullOrEmpty(channelKey)) { return _fail("No ChannelKey passed"); }
            if (VivoxService.Instance == null) { return _fail("No Vivox instance"); }
            if (action == null) { return _fail("No action passed"); }

            if (VivoxService.Instance.ActiveChannels.TryGetValue(channelKey, out var participants))
            {
                //Log.Info($"{participants.Count} participants on {channelKey}");
                foreach (var curParticipant in participants)
                {
                    action(curParticipant);
                }
            }
            else
            {
                return _fail($"No Active Vivox Channel '{channelKey}' found.");
            }

            return BoolAndReason.Success;
        }

        public BoolAndReason ForPlayerOnChannel(string channelKey, string playerID, Action<VivoxParticipant> action)
        {
            BoolAndReason _fail(string reason)
            {
                string failMsg = $"Failed to get Vivox player : {reason}";
                Log.Warn(failMsg);
                return failMsg;
            }

            if (LobbyController == null)          { return _fail("No lobby controller");  }
            if (string.IsNullOrEmpty(channelKey)) { return _fail("No ChannelKey passed"); }
            if (string.IsNullOrEmpty(playerID))   { return _fail("No PlayerID passed");   }
            if (VivoxService.Instance == null)    { return _fail("No Vivox instance");    }
            if (action == null)                   { return _fail("No action passed");     }

            if (channelKey == "*")
            {
                foreach (var curChannel in VivoxService.Instance.ActiveChannels)
                {
                    var participants = curChannel.Value;
                    foreach (var curParticipant in participants)
                    {
                        if (curParticipant.PlayerId != playerID) { continue; }

                        action(curParticipant);
                    }
                }
            }
            else
            {
                if (VivoxService.Instance.ActiveChannels.TryGetValue(channelKey, out var participants))
                {
                    foreach (var curParticipant in participants)
                    {
                        if (curParticipant.PlayerId != playerID) { continue; }

                        action(curParticipant);
                    }
                }
                else
                {
                    return _fail($"No Active Vivox Channel '{channelKey}' found.");
                }
            }

            return BoolAndReason.Success;
        }

        public VivoxParticipant GetPlayerOnChannel(string channelKey, string playerID)
        {
            VivoxParticipant _fail(string reason)
            {
                Log.Warn($"Failed to get Vivox player : {reason}");
                return null;
            }

            if (LobbyController == null)          { return _fail("No lobby controller");  }
            if (string.IsNullOrEmpty(channelKey)) { return _fail("No ChannelKey passed"); }
            if (string.IsNullOrEmpty(playerID))   { return _fail("No PlayerID passed");   }
            if (VivoxService.Instance == null)    { return _fail("No Vivox instance");    }

            if (VivoxService.Instance.ActiveChannels.TryGetValue(channelKey, out var participants))
            {
                foreach (var curParticipant in participants)
                {
                    if (curParticipant.PlayerId != playerID) { continue; }

                    return curParticipant;
                }
            }
            else
            {
                return _fail($"No Active Vivox Channel '{channelKey}' found.");
            }

            return null;
        }
        #endregion

        #region Local Player Voice Options

        public async void DisableOutputDevice()
        {
            if(Services.Vivox == null) { return; }
            var res = await Services.Vivox.SelectOutputDeviceByName("No Device");

            //foreach (var outputDevice in VivoxService.Instance.AvailableOutputDevices)
            //{
            //    if (outputDevice.DeviceName == "No Device")
            //    {
            //        VivoxService.Instance.SetActiveOutputDeviceAsync(outputDevice);
            //    }
            //}
        }

        public void EnableDefaultOutputDevice()
        {
            if (Services.Vivox == null) { return; }

            foreach (var outputDevice in VivoxService.Instance.AvailableOutputDevices)
            {
                if (outputDevice.DeviceName.Contains("Default"))
                {
                    VivoxService.Instance.SetActiveOutputDeviceAsync(outputDevice);
                }
            }
        }
        #endregion

        #region Remote Player Voice Options
        public void MuteRemotePlayer(string playerID, bool mute)
        {
            if (Services.Vivox == null) { return; }

            if (mute)
                ForPlayerOnChannel(CurLobbyChannel, playerID, MuteRemoteParticipant);
            else
                ForPlayerOnChannel(CurLobbyChannel, playerID, UnMuteRemoteParticipant);
        }

        private void MuteRemoteParticipant(VivoxParticipant participant)
        {
            if (Services.Vivox == null) { return; }

            participant.MutePlayerLocally();
            Log.Info($"Muting remote participant: {participant.DisplayName}");
        }

        private void UnMuteRemoteParticipant(VivoxParticipant participant)
        {
            if (Services.Vivox == null) { return; }

            participant.UnmutePlayerLocally();
            Log.Info($"UnMuting remote participant: {participant.DisplayName}");
        }

        public void SetRemotePlayerVolume(string playerID, int volume)
        {
            if (Services.Vivox == null) { return; }

            if (playerID == myPlayerID) return;
            if (VivoxService.Instance.ActiveChannels.TryGetValue(CurLobbyChannel, out var participants))
            {
                foreach (var curParticipant in participants)
                {
                    if (curParticipant.PlayerId != playerID) { continue; }
                    Log.Info($"Setting {curParticipant.DisplayName} volume to {volume}");
                    curParticipant.SetLocalVolume(volume);
                }
            }
            else
            {
                Log.Error($"No Active Vivox Channel '{CurLobbyChannel}' found.");
            }
        }

        //public bool IsRemotePlayerMuted(string playerID)
        //{
        //    if (VivoxService.Instance.ActiveChannels.TryGetValue(CurLobbyChannel, out var participants))
        //    {
        //        foreach (var curParticipant in participants)
        //        {
        //            if (curParticipant.PlayerId != playerID) { continue; }
        //                return curParticipant.IsMuted;
        //        }
        //        return false;
        //    }
        //    else
        //    {
        //        Log.Error($"No Active Vivox Channel '{CurLobbyChannel}' found.");
        //    }
        //}

        //public float RemotePlayerLocalVolume(string playerID)
        //{
        //    if (VivoxService.Instance.ActiveChannels.TryGetValue(CurLobbyChannel, out var participants))
        //    {
        //        foreach (var curParticipant in participants)
        //        {
        //            if (curParticipant.PlayerId != playerID) { continue; }
        //            return curParticipant.LocalVolume;
        //        }
        //        return 0;
        //    }
        //    else
        //    {
        //        Log.Error($"No Active Vivox Channel '{CurLobbyChannel}' found.");
        //    }
        //}

        #endregion
    }
}