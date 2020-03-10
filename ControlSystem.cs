using System;
using System.Collections.Generic;                       // For List, Dictionaries, etc
using Crestron.SimplSharp;                          	// For Basic SIMPL# Classes
using Crestron.SimplSharp.CrestronIO;                   // C# IO, such as Directory
using Crestron.SimplSharpPro;                       	// For Basic SIMPL#Pro classes
using Crestron.SimplSharpPro.CrestronThread;        	// For Threading
using Crestron.SimplSharpPro.Diagnostics;		    	// For System Monitor Access
using Crestron.SimplSharpPro.DeviceSupport;         	// For Generic Device Support
using Crestron.SimplSharpPro.UI;
using Daniels.Authentication;
using Daniels.Common;
using Daniels.UI;

namespace CrestronSSProUIExtensionsExample
{
    public class ControlSystem : CrestronControlSystem
    {
        private enum eIpId : uint
        {
            DSP = 0x71,
            AMP = 0x72,
            CenCi31 = 0x73,
            RFGW = 0x74,
            xPanel = 0x75,
            AdminApp = 0x76,
            tswPanel = 0x77,
        }

        private XpanelForSmartGraphics xPanel;
        private readonly string xPanelDescription = "Control xPanel";
        private readonly string xPanelSgdFileName = "CrestronSSProUIExtensionsExample.sgd";

        private IAuthenticationProvider pinAuthProvider;

        private PinLockSubPageParameters pinLockSubPageParameters = new PinLockSubPageParameters()
        {
            Id = 101,
            Name = "Pin Lock Page",
            VisibilityJoin = 101,
            TransitionJoin = 101,
            BooleanOffset = 1100,
            AnalogOffset = 1100,
            SerialOffset = 1100,
            KeyPadSmartObjectId = 1,
            AuthErrorJoin = 1,
            PinJoins = new List<uint>(4) { 1, 2, 3, 4 }
        };

        private SmartObjectReferenceListHelperParameters smartObjectReferenceListParameters = new SmartObjectReferenceListHelperParameters()
        {
            Id = 3,
            Name = "Test SLR",
            DigitalIncrement = 5,
            AnalogIncrement = 3,
            SerialIncrement = 2,
        };

        private SmartObjectDynamicListHelperParameters smartObjectDynamicListHelperParameters = new SmartObjectDynamicListHelperParameters()
        {
            Id = 6,
            Name = "Test Dynamic Icon List",
        };

        
        private ReadOnlyDictionary<string, Action<bool>> _actions = new ReadOnlyDictionary<string,Action<bool>>(
            new Dictionary<string, Action<bool>>()
            {
                {
                    "Action 1", new Action<bool>(x => 
                        {
                            if(!x)
                            {
                                CrestronConsole.PrintLine("Action 1");
                                //Action1();
                            }
                        }
                    )
                },
                {"Action 2", new Action<bool>(x => {if(!x){CrestronConsole.PrintLine("Action 2");}})},
                {"Action 3", new Action<bool>(x => {if(!x){CrestronConsole.PrintLine("Action 3");}})},
                {"Action 4", new Action<bool>(x => {if(!x){CrestronConsole.PrintLine("Action 4");}})},
                {"Action 5", new Action<bool>(x => {if(!x){CrestronConsole.PrintLine("Action 5");}})},
            }
            );

        #region Constructors

        /// <summary>
        /// ControlSystem Constructor. Starting point for the SIMPL#Pro program.
        /// Use the constructor to:
        /// * Initialize the maximum number of threads (max = 400)
        /// * Register devices
        /// * Register event handlers
        /// * Add Console Commands
        /// 
        /// Please be aware that the constructor needs to exit quickly; if it doesn't
        /// exit in time, the SIMPL#Pro program will exit.
        /// 
        /// You cannot send / receive data in the constructor
        /// </summary>
        public ControlSystem()
            : base()
        {
            try
            {
                Thread.MaxNumberOfUserThreads = 20;

                // * Register devices

                xPanel = new XpanelForSmartGraphics((uint)eIpId.xPanel, this);
                xPanel.Description = xPanelDescription;
                // Add SGD to tswPanel
                string xPanelSgdFilePath = Directory.GetApplicationDirectory() + Path.DirectorySeparatorChar + xPanelSgdFileName;
                // make sure file exists in the application directory
                if (File.Exists(xPanelSgdFilePath))
                {
                    // load the SGD file for this ui project
                    try
                    {
                        xPanel.LoadSmartObjects(xPanelSgdFilePath);
                    }
                    catch (Exception)
                    {
                        ErrorLog.Error(">>> {0} (IPID 0x{1:X2}) Could not find {0} SGD file. SmartObjects will not work at this time", xPanel.Name, (uint)eIpId.xPanel, xPanelSgdFilePath);
                    }
                    ErrorLog.Notice(">>> {0} (IPID 0x{1:X2}) SmartObjects loaded from {2}", xPanel.Name, (uint)eIpId.xPanel, xPanelSgdFileName);
                }
                else
                {
                    ErrorLog.Error(">>> {0} (IPID 0x{1:X2}) Could not find {2} SGD file. SmartObjects will not work at this time", xPanel.Name, (uint)eIpId.xPanel, xPanelSgdFilePath);
                }

                if (xPanel.Register() == eDeviceRegistrationUnRegistrationResponse.Success)
                    ErrorLog.Notice(">>> {0} (IPID 0x{1:X2}) has been registered successfully", xPanel.Name, (uint)eIpId.xPanel);
                else
                    ErrorLog.Error(">>> {0} (IPID 0x{1:X2})  was not registered: {2}", xPanel.Name, (uint)eIpId.xPanel, xPanel.RegistrationFailureReason);

                xPanel.UserSpecifiedObject = new AuthenticatedSubPageManager(
                    Authenticate,
                    xPanel,
                    new PinLockSubPage(pinLockSubPageParameters),
                    60000,
                    new List<SubPage>() { }
                    );

                // * Register event handlers

                //Subscribe to the controller events (System, Program, and Ethernet)
                CrestronEnvironment.SystemEventHandler += new SystemEventHandler(ControlSystem_ControllerSystemEventHandler);
                CrestronEnvironment.ProgramStatusEventHandler += new ProgramStatusEventHandler(ControlSystem_ControllerProgramEventHandler);
                CrestronEnvironment.EthernetEventHandler += new EthernetEventHandler(ControlSystem_ControllerEthernetEventHandler);

                xPanel.SigChange += new SigEventHandler(EventHandlers.ActionEventHandler);
                xPanel.SmartObjects[2].SigChange += new SmartObjectSigChangeEventHandler(EventHandlers.ActionEventHandler);

                xPanel.BooleanOutput[100].UserObject = new Action<bool>(x => { if (!x) ((AuthenticatedSubPageManager)xPanel.UserSpecifiedObject).Lock(); });

                // local tests
                xPanel.SigChange += new SigEventHandler(panel_SigChange);
                xPanel.BaseEvent += new BaseEventHandler(xPanel_BaseEvent);
                foreach (var kv in xPanel.SmartObjects)
                {
                    kv.Value.SigChange += new SmartObjectSigChangeEventHandler(so_SigChange);
                }

                ((AuthenticatedSubPageManager)xPanel.UserSpecifiedObject).Authenticated += new EventHandler<AuthenticatedSubPageManager.AuthenticatedEventArgs>(ControlSystem_Authenticated);

                // Panels Smart Objects

                SmartObjectReferenceListHelper smartObjectReferenceListHelper = new SmartObjectReferenceListHelper(xPanel.SmartObjects[smartObjectReferenceListParameters.Id], smartObjectReferenceListParameters);
                xPanel.SmartObjects[smartObjectReferenceListParameters.Id].SigChange += new SmartObjectSigChangeEventHandler(EventHandlers.ActionEventHandler);
                List<int> range = new List<int>() { 1, 2 };
                smartObjectReferenceListHelper.NumberOfItems = (ushort)range.Count;
                foreach (uint itemIndex in range)
                {
                    uint itemIndexFix = itemIndex;
                    SmartObjectReferenceListItem item = smartObjectReferenceListHelper.Items[itemIndex];
                    smartObjectReferenceListHelper.Items[itemIndex].BooleanOutput[1].UserObject = new Action<bool>(x =>
                    {
                        if (x)
                        {
                            CrestronConsole.PrintLine("Item: {0}", item.Id);
                            item.UShortInput[1].UShortValue = (ushort)(item.UShortInput[1].UShortValue + 1);
                        }
                    });
                }

                xPanel.SmartObjects[smartObjectDynamicListHelperParameters.Id].SigChange += new SmartObjectSigChangeEventHandler(EventHandlers.ActionEventHandler);

                CrestronConsole.AddNewConsoleCommand(ConsoleCommandTest, "test", "Test plug", ConsoleAccessLevelEnum.AccessOperator);
                ActionsManager.RegisterAction(Action1, "Action 1", "Action1 description");
                ActionsManager.RegisterAction(Action2, "Action 2", "Action2 description");
                ActionsManager.RegisterAction(Action2, "Action 2", "Action2 description", "special Action 2", "special Action2 params");
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in the constructor: {0}\r\n{1}", e.Message, e.StackTrace);
            }
        }

        /// <summary>
        /// InitializeSystem - this method gets called after the constructor 
        /// has finished. 
        /// 
        /// Use InitializeSystem to:
        /// * Start threads
        /// * Configure ports, such as serial and verisports
        /// * Start and initialize socket connections
        /// Send initial device configurations
        /// 
        /// Please be aware that InitializeSystem needs to exit quickly also; 
        /// if it doesn't exit in time, the SIMPL#Pro program will exit.
        /// </summary>
        public override void InitializeSystem()
        {
            try
            {
                if (PinAuthenticationProvider.Initialize() == PinAuthenticationProvider.InitializationSuccessFailureReason.ConfigFileNotFound)
                    PinAuthenticationProvider.CreateNewConfig();
                pinAuthProvider = PinAuthenticationProvider.GetInstance();

                SmartObjectDynamicButtonIconListHelper smartObjectDynamicButtonIconListHelper = new SmartObjectDynamicButtonIconListHelper(xPanel.SmartObjects[smartObjectDynamicListHelperParameters.Id], smartObjectDynamicListHelperParameters);
                smartObjectDynamicButtonIconListHelper.NumberOfItems = (ushort)ActionsManager.GetActions().Length;
                uint actionId = 0;
                foreach (string actionName in ActionsManager.GetActions())
                {
                    actionId++;
                    string actionNameFix = actionName;
                    smartObjectDynamicButtonIconListHelper.Items[actionId].Enable = true;
                    smartObjectDynamicButtonIconListHelper.Items[actionId].Visible = true;
                    smartObjectDynamicButtonIconListHelper.Items[actionId].Text = actionNameFix;
                    smartObjectDynamicButtonIconListHelper.Items[actionId].PressedAction = new Action<bool>(x => { if (!x) ActionsManager.InvokeAction(actionNameFix); });
                }
            }
            catch (Exception e)
            {
                ErrorLog.Error("Error in InitializeSystem: {0}", e.Message);
            }
        }

        #endregion Constructors

        #region Event Handlers: System

        /// <summary>
        /// Event Handler for Ethernet events: Link Up and Link Down. 
        /// Use these events to close / re-open sockets, etc. 
        /// </summary>
        /// <param name="ethernetEventArgs">This parameter holds the values 
        /// such as whether it's a Link Up or Link Down event. It will also indicate 
        /// wich Ethernet adapter this event belongs to.
        /// </param>
        void ControlSystem_ControllerEthernetEventHandler(EthernetEventArgs ethernetEventArgs)
        {
            switch (ethernetEventArgs.EthernetEventType)
            {//Determine the event type Link Up or Link Down
                case (eEthernetEventType.LinkDown):
                    //Next need to determine which adapter the event is for. 
                    //LAN is the adapter is the port connected to external networks.
                    if (ethernetEventArgs.EthernetAdapter == EthernetAdapterType.EthernetLANAdapter)
                    {
                        //
                    }
                    break;
                case (eEthernetEventType.LinkUp):
                    if (ethernetEventArgs.EthernetAdapter == EthernetAdapterType.EthernetLANAdapter)
                    {

                    }
                    break;
            }
        }

        /// <summary>
        /// Event Handler for Programmatic events: Stop, Pause, Resume.
        /// Use this event to clean up when a program is stopping, pausing, and resuming.
        /// This event only applies to this SIMPL#Pro program, it doesn't receive events
        /// for other programs stopping
        /// </summary>
        /// <param name="programStatusEventType"></param>
        void ControlSystem_ControllerProgramEventHandler(eProgramStatusEventType programStatusEventType)
        {
            switch (programStatusEventType)
            {
                case (eProgramStatusEventType.Paused):
                    //The program has been paused.  Pause all user threads/timers as needed.
                    break;
                case (eProgramStatusEventType.Resumed):
                    //The program has been resumed. Resume all the user threads/timers as needed.
                    break;
                case (eProgramStatusEventType.Stopping):
                    //The program has been stopped.
                    //Close all threads. 
                    //Shutdown all Client/Servers in the system.
                    //General cleanup.
                    //Unsubscribe to all System Monitor events
                    break;
            }

        }

        /// <summary>
        /// Event Handler for system events, Disk Inserted/Ejected, and Reboot
        /// Use this event to clean up when someone types in reboot, or when your SD /USB
        /// removable media is ejected / re-inserted.
        /// </summary>
        /// <param name="systemEventType"></param>
        void ControlSystem_ControllerSystemEventHandler(eSystemEventType systemEventType)
        {
            switch (systemEventType)
            {
                case (eSystemEventType.DiskInserted):
                    //Removable media was detected on the system
                    break;
                case (eSystemEventType.DiskRemoved):
                    //Removable media was detached from the system
                    break;
                case (eSystemEventType.Rebooting):
                    //The system is rebooting. 
                    //Very limited time to preform clean up and save any settings to disk.
                    break;
            }

        }

        #endregion Event Handlers: System

        #region Event Handlers: UI

        void panel_SigChange(BasicTriList currentDevice, SigEventArgs args)
        {
            //CrestronConsole.PrintLine("{0}: Event:{1} Type:{2} Number:{3}", currentDevice.Name, args.Event, args.Sig.Type, args.Sig.Number);
            if(args.Sig.Type == eSigType.Bool && args.Sig.BoolValue && args.Sig.Number < 17000)
                CrestronConsole.PrintLine("Activity: {0}", args.Sig.Number);
        }

        void xPanel_BaseEvent(GenericBase device, BaseEventArgs args)
        {
            CrestronConsole.PrintLine("{0}: EventId:{1} Index:{2} Text:{3}", device.Name, args.EventId, args.Index, args.ToString());
        }

        void so_SigChange(GenericBase currentDevice, SmartObjectEventArgs args)
        {
            //CrestronConsole.PrintLine("{0}: SO:{4} Event:{1} Type:{2} Number:{3}", currentDevice.Name, args.Event, args.Sig.Type, args.Sig.Number, args.SmartObjectArgs.ID);
            if (args.Sig.Type == eSigType.Bool && args.Sig.BoolValue)
                CrestronConsole.PrintLine("Activity: {0}", args.Sig.Number);
        }

        #endregion Event Handlers: UI

        #region Event Handlers

        void ControlSystem_Authenticated(object sender, AuthenticatedSubPageManager.AuthenticatedEventArgs e)
        {
            AuthenticatedSubPageManager subPageManager = sender as AuthenticatedSubPageManager;
            if (e.Level == AuthenticatedLevel.None)
            {
                subPageManager.Panel.BooleanInput[100].BoolValue = true;
            }
            else
                subPageManager.Panel.BooleanInput[100].BoolValue = false;
        }

        AuthenticatedLevel Authenticate(ushort pin, out string user)
        {
            AuthorizationLevel authorizationLevel = pinAuthProvider.AuthorizePin(pin, out user);
            AuthenticatedLevel authenticatedLevel = AuthenticatedLevel.None;
            switch(authorizationLevel)
            {
                case AuthorizationLevel.Level1:
                    authenticatedLevel = AuthenticatedLevel.Level1;
                    break;
                case AuthorizationLevel.Level2:
                    authenticatedLevel = AuthenticatedLevel.Level2;
                    break;
            }
            return authenticatedLevel;
        }

        #endregion Event Handlers

        private void Action1(string actionParameters)
        {
            CrestronConsole.PrintLine("Action1: {0}", String.IsNullOrEmpty(actionParameters) ? "no params" : actionParameters);

            SmartObjectDynamicButtonIconListHelper smartObjectDynamicButtonIconListHelper = new SmartObjectDynamicButtonIconListHelper(xPanel.SmartObjects[smartObjectDynamicListHelperParameters.Id], smartObjectDynamicListHelperParameters);

            smartObjectDynamicButtonIconListHelper.NumberOfItems = (ushort)_actions.Count;

            CrestronConsole.PrintLine("Action1: Item[1] was {0}", smartObjectDynamicButtonIconListHelper.Items[1].Selected);
            smartObjectDynamicButtonIconListHelper.Items[1].Selected = !smartObjectDynamicButtonIconListHelper.Items[1].Selected;
        }

        private void Action2(string actionParameters)
        {
            CrestronConsole.PrintLine("Action2: {0}", String.IsNullOrEmpty(actionParameters) ? "no params" : actionParameters);

            if (String.IsNullOrEmpty(actionParameters))
            {
                SmartObjectDynamicButtonIconListHelper smartObjectDynamicButtonIconListHelper = new SmartObjectDynamicButtonIconListHelper(xPanel.SmartObjects[smartObjectDynamicListHelperParameters.Id], smartObjectDynamicListHelperParameters);

                smartObjectDynamicButtonIconListHelper.NumberOfItems = (ushort)_actions.Count;

                CrestronConsole.PrintLine("Action2: Item[2] was {0}", smartObjectDynamicButtonIconListHelper.Items[2].Selected);
                smartObjectDynamicButtonIconListHelper.Items[2].Selected = !smartObjectDynamicButtonIconListHelper.Items[2].Selected;
            }
        }


        #region Console Methods

        /// <summary>
        /// Test Console functions.
        /// </summary>
        /// <param name="cmd">command name</param>
        private void ConsoleCommandTest(string cmd)
        {

            //foreach(dd in DMOutputEventIds.VideoOutEventId)

            //CrestronConsole.ConsoleCommandResponse("VideoOutEventId:{0}", DMOutputEventIds.VideoOutEventId);
            
            //CrestronConsole.ConsoleCommandResponse("Locking");

            //AuthenticatedSubPageManager authManager = xPanel.UserSpecifiedObject as AuthenticatedSubPageManager;

            //CrestronConsole.ConsoleCommandResponse("Test: \r\n{0}", authManager.ToString());

            //xPanel.SmartObjects[smartObjectReferenceListParameters.Id].UShortInput["Set Number of Items"].UserObject = null;



            CrestronConsole.PrintLine("Debug10");

            /*
            SmartObjectDynamicButtonIconListHelper smartObjectDynamicButtonIconListHelper = new SmartObjectDynamicButtonIconListHelper(xPanel.SmartObjects[smartObjectDynamicListHelperParameters.Id], smartObjectDynamicListHelperParameters);
            xPanel.SmartObjects[smartObjectDynamicListHelperParameters.Id].SigChange += new SmartObjectSigChangeEventHandler(EventHandlers.ActionEventHandler);

            smartObjectDynamicButtonIconListHelper.NumberOfItems = (ushort)_actions.Count;

            uint actionId = 0;
            foreach (var kv in _actions)
            {
                actionId++;
                //string actionName = kv.Key;

                //Action<bool> action = kv.Value; // Fix for .NET3.5
                smartObjectDynamicButtonIconListHelper.Items[actionId].Enable = true;
                smartObjectDynamicButtonIconListHelper.Items[actionId].Visible = true;
                smartObjectDynamicButtonIconListHelper.Items[actionId].Text = kv.Key;
                smartObjectDynamicButtonIconListHelper.Items[actionId].PressedAction = kv.Value;
            }
            */
            //smartObjectDynamicButtonIconListHelper.Items[6].Enable = false;
            //smartObjectDynamicButtonIconListHelper.Items[5].Visible = true;
            //smartObjectDynamicButtonIconListHelper.Items[5].Enable = true;
            //smartObjectDynamicButtonIconListHelper.Items[5].Text = "Action 5";

            CrestronConsole.PrintLine("Debug11");
        }

        #endregion Console Methods

    }
}