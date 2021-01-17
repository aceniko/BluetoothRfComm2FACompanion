//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using Bluetooth.Proximity.BusinessObjects.Constants;
using Bluetooth.Proximity.BusinessObjects.Device;
using Bluetooth.Proximity.BusinessObjects.Enums;
using Bluetooth.Proximity.BusinessObjects.TransportModels;
using Bluetooth.Proximity.Connector;
using Bluetooth.Proximity.Connector.DeviceWatcher;
using Bluetooth.Proximity.Connector.Socket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Security.Authentication.Identity.Provider;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace SDKTemplate
{
    public sealed partial class DeviceManager : Page
    {
        // A pointer back to the main page is required to display status messages.
        private MainPage rootPage = MainPage.Current;

        // Used to display list of available devices to chat with
        public ObservableCollection<RfcommDeviceDisplay> ResultCollection
        {
            get;
            private set;
        }

        private DeviceWatcherService deviceWatcherService;
        private BluetoothDeviceManager bluetoothDeviceManager;
        private SocketService socketService;

        //private DeviceWatcher deviceWatcher = null;
        private StreamSocket streamSocket = null;
        private DataWriter dataWriter = null;
        
        private RfcommDeviceService rfCommDeviceService = null;
        //private BluetoothDevice bluetoothDevice;

        // This App relies on CRC32 checking available in version 2.0 of the service.
        private const uint SERVICE_VERSION_ATTRIBUTE_ID = 0x0300;

        private const byte SERVICE_VERSION_ATTRIBUTE_TYPE = 0x0A;   // UINT32
        private const uint MINIMUM_SERVICE_VERSION = 200;

        bool taskRegistered = false;
        static string authBGTaskName = "CDFTask";
        static string authBGTaskEntryPoint = "BackgroundTasks.CDFTask";

        public DeviceManager()
        {
            this.InitializeComponent();
            App.Current.Suspending += App_Suspending;
            bluetoothDeviceManager = new BluetoothDeviceManager();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            rootPage = MainPage.Current;
            ResultCollection = new ObservableCollection<RfcommDeviceDisplay>();
            DataContext = this;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (deviceWatcherService != null) { 
                deviceWatcherService.StopWatcher();
            }
        }



        void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            // Make sure we clean up resources on suspend.
            Disconnect("App Suspension disconnects");
        }

        #region Click Events

        /// <summary>
        /// When the user presses the run button, query for all nearby unpaired devices
        /// Note that in this case, the other device must be running the Rfcomm Chat Server before being paired.
        /// </summary>
        /// <param name="sender">Instance that triggered the event.</param>
        /// <param name="e">Event data describing the conditions that led to the event.</param>
        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (deviceWatcherService == null || deviceWatcherService.GetDeviceWatcher() == null)
            {
                SetDeviceWatcherUI();
                deviceWatcherService = new DeviceWatcherService(rootPage.Dispatcher);
                SetDeviceWatcherUiEvents();
                deviceWatcherService.StartDeviceWatcherService();
            }
            else
            {
                ResetMainUI();
            }
        }

        private async void PairDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            RfcommDeviceDisplay deviceInfoDisp = resultsListView.SelectedItem as RfcommDeviceDisplay;

            DevicePairingResult dpr = await deviceInfoDisp.DeviceInformation.Pairing.PairAsync();

            rootPage.NotifyUser(
                "Pairing result = " + dpr.Status.ToString(),
                dpr.Status == DevicePairingResultStatus.Paired ? NotifyType.StatusMessage : NotifyType.ErrorMessage);
        }

        /// <summary>
        /// Invoked once the user has selected the device to connect to.
        /// Once the user has selected the device,
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Make sure user has selected a device first
                if (resultsListView.SelectedItem != null)
                {
                    rootPage.NotifyUser("Connecting to remote device. Please wait...", NotifyType.StatusMessage);
                }
                else
                {
                    rootPage.NotifyUser("Please select an item to connect to", NotifyType.ErrorMessage);
                    return;
                }

                RfcommDeviceDisplay deviceInfoDisp = resultsListView.SelectedItem as RfcommDeviceDisplay;

                // Perform device access checks before trying to get the device.
                // First, we check if consent has been explicitly denied by the user.
                bluetoothDeviceManager.CheckConsent(deviceInfoDisp.Id);


                // If not, try to get the Bluetooth device
                try
                {
                    await bluetoothDeviceManager.GetBluetoothDeviceById(deviceInfoDisp.Id);
                }
                catch (Exception ex)
                {
                    rootPage.NotifyUser(ex.Message, NotifyType.ErrorMessage);
                    ResetMainUI();
                    return;
                }



                //var services = await bluetoothDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);

                // This should return a list of uncached Bluetooth services (so if the server was not active when paired, it will still be detected by this call

                try
                {
                    rfCommDeviceService = await bluetoothDeviceManager.GetRfCommDeviceService(Constants.RfcommServiceUuid);
                }
                catch (Exception ex)
                {
                    rootPage.NotifyUser(
                           ex.Message,
                           NotifyType.StatusMessage);
                    ResetMainUI();
                    return;
                }







                // Do various checks of the SDP record to make sure you are talking to a device that actually supports the Bluetooth Rfcomm Chat Service
                var attributes = await rfCommDeviceService.GetSdpRawAttributesAsync();
                if (!attributes.ContainsKey(Constants.SdpServiceNameAttributeId))
                {
                    rootPage.NotifyUser(
                        "The Chat service is not advertising the Service Name attribute (attribute id=0x100). " +
                        "Please verify that you are running the BluetoothRfcommChat server.",
                        NotifyType.ErrorMessage);
                    ResetMainUI();
                    return;
                }
                var attributeReader = DataReader.FromBuffer(attributes[Constants.SdpServiceNameAttributeId]);
                var attributeType = attributeReader.ReadByte();
                if (attributeType != Constants.SdpServiceNameAttributeType)
                {
                    rootPage.NotifyUser(
                        "The Chat service is using an unexpected format for the Service Name attribute. " +
                        "Please verify that you are running the BluetoothRfcommChat server.",
                        NotifyType.ErrorMessage);
                    ResetMainUI();
                    return;
                }
                var serviceNameLength = attributeReader.ReadByte();

                // The Service Name attribute requires UTF-8 encoding.
                attributeReader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;

                deviceWatcherService.StopWatcher();

                socketService = new SocketService();


                try
                {
                    await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => {
                        socketService.ConnectAsync(rfCommDeviceService, true);
                    });
                    

                    //await chatSocket.ConnectAsync(chatService.ConnectionHostName, chatService.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

                    //dataReader = new DataReader(chatSocket.InputStream);
                    //chatWriter = new DataWriter(chatSocket.OutputStream);

                    SetChatUI(attributeReader.ReadString(serviceNameLength), bluetoothDeviceManager.GetBluetoothDevice().Name);


                    //ReceiveStringLoopAsync();

                }
                catch (Exception ex) when ((uint)ex.HResult == 0x80070490) // ERROR_ELEMENT_NOT_FOUND
                {
                    rootPage.NotifyUser("Please verify that you are running the BluetoothRfcommChat server.", NotifyType.ErrorMessage);
                    ResetMainUI();
                }
                catch (Exception ex) when ((uint)ex.HResult == 0x80072740) // WSAEADDRINUSE
                {
                    rootPage.NotifyUser("Please verify that there is no other RFCOMM connection to the same device.", NotifyType.ErrorMessage);
                    ResetMainUI();
                }
            }
            catch (Exception ex)
            {
                rootPage.NotifyUser(ex.Message, NotifyType.ErrorMessage);
            }
        }

        /// <summary>
        ///  If you believe the Bluetooth device will eventually be paired with Windows,
        ///  you might want to pre-emptively get consent to access the device.
        ///  An explicit call to RequestAccessAsync() prompts the user for consent.
        ///  If this is not done, a device that's working before being paired,
        ///  will no longer work after being paired.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void RequestAccessButton_Click(object sender, RoutedEventArgs e)
        {
            // Make sure user has given consent to access device


            DeviceAccessStatus accessStatus = await bluetoothDeviceManager.RequestAccess();



            if (accessStatus != DeviceAccessStatus.Allowed)
            {
                rootPage.NotifyUser(
                    "Access to the device is denied because the application was not granted access",
                    NotifyType.StatusMessage);
            }
            else
            {
                rootPage.NotifyUser(
                                    "Access granted, you are free to pair devices",
                                    NotifyType.StatusMessage);
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {

        }

        public void KeyboardKey_Pressed(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {

            }
        }

        public void RegisterDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            RegisterDeviceRequest model = new RegisterDeviceRequest { RequestCommand = Commands.REQUEST_KEYS };

            socketService.SendMessage(JsonConvert.SerializeObject(model));

        }

        public async void RefreshDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDeviceListAsync();
        }

        public async void UnregisterDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            if (rootPage.GetSelectedDeviceId() == String.Empty)
            {
                return;
            }

            //InfoList.Items.Add("Unregister a device:");

            await SecondaryAuthenticationFactorRegistration.UnregisterDeviceAsync(rootPage.GetSelectedDeviceId());

            //InfoList.Items.Add("Device unregistration is completed.");



            await RefreshDeviceListAsync();
        }

        public async void RegisterBackgroundTask_Click(object sender, RoutedEventArgs e)
        {
            RegisterTask();

            

            
        }
        #endregion



        private void SetDeviceWatcherUI()
        {
            // Disable the button while we do async operations so the user can't Run twice.
            RunButton.Content = "Stop";
            rootPage.NotifyUser("Device watcher started", NotifyType.StatusMessage);
            resultsListView.Visibility = Visibility.Visible;
            resultsListView.IsEnabled = true;
        }

        private void ResetMainUI()
        {
            RunButton.Content = "Start";
            RunButton.IsEnabled = true;
            ConnectButton.Visibility = Visibility.Visible;
            resultsListView.Visibility = Visibility.Visible;
            resultsListView.IsEnabled = true;

            // Re-set device specific UX
            ChatBox.Visibility = Visibility.Collapsed;
            RequestAccessButton.Visibility = Visibility.Collapsed;
            if (ConversationList.Items != null) ConversationList.Items.Clear();
            deviceWatcherService.StopWatcher();
        }

        private void SetDeviceWatcherUiEvents()
        {
            // Hook up handlers for the watcher events before starting the watcher
            deviceWatcherService.AddedUIEvent(new TypedEventHandler<DeviceWatcher, DeviceInformation>(async (watcher, deviceInfo) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    // Make sure device name isn't blank
                    if (deviceInfo.Name != "")
                    {
                        ResultCollection.Add(new RfcommDeviceDisplay(deviceInfo));
                        rootPage.NotifyUser(
                            String.Format("{0} devices found.", ResultCollection.Count),
                            NotifyType.StatusMessage);
                    }

                });
            }));

            deviceWatcherService.UpdatedUIEvent(new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    foreach (RfcommDeviceDisplay rfcommInfoDisp in ResultCollection)
                    {
                        if (rfcommInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            rfcommInfoDisp.Update(deviceInfoUpdate);
                            break;
                        }
                    }
                });
            }));

            deviceWatcherService.EnumerationCompletedUIEvent(new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    rootPage.NotifyUser(
                        String.Format("{0} devices found. Enumeration completed. Watching for updates...", ResultCollection.Count),
                        NotifyType.StatusMessage);
                });
            }));

            deviceWatcherService.RemovedUIEvent(new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(async (watcher, deviceInfoUpdate) =>
            {
                // Since we have the collection databound to a UI element, we need to update the collection on the UI thread.
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    // Find the corresponding DeviceInformation in the collection and remove it
                    foreach (RfcommDeviceDisplay rfcommInfoDisp in ResultCollection)
                    {
                        if (rfcommInfoDisp.Id == deviceInfoUpdate.Id)
                        {
                            ResultCollection.Remove(rfcommInfoDisp);
                            break;
                        }
                    }

                    rootPage.NotifyUser(
                        String.Format("{0} devices found.", ResultCollection.Count),
                        NotifyType.StatusMessage);
                });
            }));

            deviceWatcherService.StoppedUIEvent(new TypedEventHandler<DeviceWatcher, Object>(async (watcher, obj) =>
            {
                await rootPage.Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    ResultCollection.Clear();
                });
            }));

            //deviceWatcherService.StartDeviceWatcherService();
        }






        /// <summary>
        /// Takes the contents of the MessageTextBox and writes it to the outgoing chatWriter
        /// </summary>
        


        

        

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Disconnect("Disconnected");
        }


        /// <summary>
        /// Cleans up the socket and DataWriter and reset the UI
        /// </summary>
        /// <param name="disconnectReason"></param>
        private void Disconnect(string disconnectReason)
        {
            if (dataWriter != null)
            {
                dataWriter.DetachStream();
                dataWriter = null;
            }


            if (rfCommDeviceService != null)
            {
                rfCommDeviceService.Dispose();
                rfCommDeviceService = null;
            }
            lock (this)
            {
                if (streamSocket != null)
                {
                    streamSocket.Dispose();
                    streamSocket = null;
                }
            }

            rootPage.NotifyUser(disconnectReason, NotifyType.StatusMessage);
            ResetMainUI();
        }

        private void SetChatUI(string serviceName, string deviceName)
        {
            rootPage.NotifyUser("Connected", NotifyType.StatusMessage);
            ServiceName.Text = "Service Name: " + serviceName;
            DeviceName.Text = "Connected to: " + deviceName;
            RunButton.IsEnabled = false;
            ConnectButton.Visibility = Visibility.Collapsed;
            RequestAccessButton.Visibility = Visibility.Visible;
            resultsListView.IsEnabled = false;
            resultsListView.Visibility = Visibility.Collapsed;
            ChatBox.Visibility = Visibility.Visible;
            RegisterDeviceButton.Visibility = Visibility.Visible;
        }

        private void ResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePairingButtons();
        }

        private void UpdatePairingButtons()
        {
            RfcommDeviceDisplay deviceDisp = (RfcommDeviceDisplay)resultsListView.SelectedItem;

            if (deviceDisp != null && deviceDisp.DeviceInformation.Pairing.CanPair && !deviceDisp.DeviceInformation.Pairing.IsPaired)
            {
                ConnectButton.IsEnabled = false;
                PairDeviceButton.IsEnabled = true;
                PairDeviceButton.Visibility = Visibility.Visible;
            }
            else if (null != deviceDisp)
            {
                ConnectButton.IsEnabled = true;
                PairDeviceButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                ConnectButton.IsEnabled = false;
            }
        }

        private bool SupportsProtection(RfcommDeviceService service)
        {
            switch (service.ProtectionLevel)
            {
                case SocketProtectionLevel.PlainSocket:
                    if ((service.MaxProtectionLevel == SocketProtectionLevel
                            .BluetoothEncryptionWithAuthentication)
                        || (service.MaxProtectionLevel == SocketProtectionLevel
                            .BluetoothEncryptionAllowNullAuthentication))
                    {
                        // The connection can be upgraded when opening the socket so the
                        // App may offer UI here to notify the user that Windows may
                        // prompt for a PIN exchange.
                        return true;
                    }
                    else
                    {
                        // The connection cannot be upgraded so an App may offer UI here
                        // to explain why a connection won't be made.
                        return false;
                    }
                case SocketProtectionLevel.BluetoothEncryptionWithAuthentication:
                    return true;

                case SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication:
                    return true;
            }
            return false;
        }

        private async System.Threading.Tasks.Task<bool> IsCompatibleVersion(RfcommDeviceService service)
        {
            var attributes = await service.GetSdpRawAttributesAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);
            var attribute = attributes[SERVICE_VERSION_ATTRIBUTE_ID];
            var reader = DataReader.FromBuffer(attribute);

            // The first byte contains the attribute' s type
            byte attributeType = reader.ReadByte();
            if (attributeType == SERVICE_VERSION_ATTRIBUTE_TYPE)
            {
                // The remainder is the data
                uint version = reader.ReadUInt32();
                return version >= MINIMUM_SERVICE_VERSION;
            }
            return false;
        }


        private async Task ProcessInputMessageAsync(string message)
        {

            string decodedJsonObject = Encoding.UTF8.GetString(System.Convert.FromBase64String(message));

            dynamic obj = JsonConvert.DeserializeObject(decodedJsonObject);

            string response_command = obj.response_command;

            switch (response_command)
            {
                case "REQUEST_KEYS_REPLY":
                    String deviceId = System.Guid.NewGuid().ToString();
                    var registerDeviceResponseModel = JsonConvert.DeserializeObject<RegisterDeviceResponse>(decodedJsonObject);

                    byte[] deviceKeyArray = { 0 };
                    byte[] authKeyArray = { 0 };




                    deviceKeyArray = System.Convert.FromBase64String(registerDeviceResponseModel.DeviceKey);
                    authKeyArray = System.Convert.FromBase64String(registerDeviceResponseModel.AuthKey);

                    var deviceKey = CryptographicBuffer.CreateFromByteArray(deviceKeyArray);
                    var authKey = CryptographicBuffer.CreateFromByteArray(authKeyArray);

                    //Generate combinedDataArray
                    int combinedDataArraySize = deviceKeyArray.Length + authKeyArray.Length;
                    byte[] combinedDataArray = new byte[combinedDataArraySize];
                    for (int index = 0; index < deviceKeyArray.Length; index++)
                    {
                        combinedDataArray[index] = deviceKeyArray[index];
                    }
                    for (int index = 0; index < authKeyArray.Length; index++)
                    {
                        combinedDataArray[deviceKeyArray.Length + index] = authKeyArray[index];
                    }

                    // Get a Ibuffer from combinedDataArray
                    IBuffer deviceConfigData = CryptographicBuffer.CreateFromByteArray(combinedDataArray);
                    SecondaryAuthenticationFactorDeviceCapabilities capabilities = SecondaryAuthenticationFactorDeviceCapabilities.SecureStorage;

                    SecondaryAuthenticationFactorRegistrationResult registrationResult = await SecondaryAuthenticationFactorRegistration.RequestStartRegisteringDeviceAsync(deviceId,
                            capabilities,
                            registerDeviceResponseModel.DeviceName,
                            registerDeviceResponseModel.DeviceModel,
                            deviceKey,
                            authKey);

                    if (registrationResult.Status != SecondaryAuthenticationFactorRegistrationStatus.Started)
                    {
                        MessageDialog myDlg = null;

                        if (registrationResult.Status == SecondaryAuthenticationFactorRegistrationStatus.DisabledByPolicy)
                        {
                            //For DisaledByPolicy Exception:Ensure secondary auth is enabled.
                            //Use GPEdit.msc to update group policy to allow secondary auth
                            //Local Computer Policy\Computer Configuration\Administrative Templates\Windows Components\Microsoft Secondary Authentication Factor\Allow Companion device for secondary authentication
                            myDlg = new MessageDialog("Disabled by Policy.  Please update the policy and try again.");
                        }

                        if (registrationResult.Status == SecondaryAuthenticationFactorRegistrationStatus.PinSetupRequired)
                        {
                            //For PinSetupRequired Exception:Ensure PIN is setup on the device
                            //Either use gpedit.msc or set reg key
                            //This setting can be enabled by creating the AllowDomainPINLogon REG_DWORD value under the HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\System Registry key and setting it to 1.
                            myDlg = new MessageDialog("Please setup PIN for your device and try again.");
                        }

                        if (myDlg != null)
                        {
                            await myDlg.ShowAsync();
                            return;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("Device Registration Started!");
                    await registrationResult.Registration.FinishRegisteringDeviceAsync(deviceConfigData);
                    rootPage.AddDeviceInRegisteredDeviceList(deviceId);

                    System.Diagnostics.Debug.WriteLine("Device Registration is Complete!");



                    await RefreshDeviceListAsync();
                    break;
            }


        }

        private async Task RefreshDeviceListAsync()
        {
            rootPage.ClearRegisteredDevicesList();

            IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceList = await SecondaryAuthenticationFactorRegistration.FindAllRegisteredDeviceInfoAsync(
                        SecondaryAuthenticationFactorDeviceFindScope.User);

            for (int index = 0; index < deviceList.Count; ++index)
            {
                SecondaryAuthenticationFactorInfo deviceInfo = deviceList.ElementAt(index);
                rootPage.AddDeviceInRegisteredDeviceList(deviceInfo.DeviceId);
            }
        }


        async void RegisterTask()
        {
            System.Diagnostics.Debug.WriteLine("[RegisterTask] Register the background task.");
            //
            // Check for existing registrations of this background task.
            //

            BackgroundExecutionManager.RemoveAccess();
            var access = await BackgroundExecutionManager.RequestAccessAsync();

            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == authBGTaskName)
                {
                    //taskRegistered = true;
                    task.Value.Unregister(true);
                    //break;
                }
            }

            if (!taskRegistered)
            {

                if (access == BackgroundAccessStatus.AllowedSubjectToSystemPolicy)
                {
                    BackgroundTaskBuilder authTaskBuilder = new BackgroundTaskBuilder();
                    authTaskBuilder.Name = authBGTaskName;
                    SecondaryAuthenticationFactorAuthenticationTrigger myTrigger = new SecondaryAuthenticationFactorAuthenticationTrigger();
                    authTaskBuilder.TaskEntryPoint = authBGTaskEntryPoint;
                    authTaskBuilder.SetTrigger(myTrigger);
                    BackgroundTaskRegistration taskReg = authTaskBuilder.Register();

                    //BackgroundTaskBuilder plugTaskBuilder = new BackgroundTaskBuilder();
                    //plugTaskBuilder.Name = authBGTaskName;
                    //plugTaskBuilder.TaskEntryPoint = authBGTaskEntryPoint;
                    //plugTaskBuilder.SetTrigger(deviceWatcherTrigger);
                    //BackgroundTaskRegistration taskReg2 = plugTaskBuilder.Register();
                    //String taskRegName = taskReg.Name;

                    //BackgroundTaskBuilder rebootTaskBuilder = new BackgroundTaskBuilder();
                    //rebootTaskBuilder.Name = authBGTaskName;
                    //rebootTaskBuilder.TaskEntryPoint = authBGTaskEntryPoint;
                    //SystemTrigger trigger = new SystemTrigger(SystemTriggerType.UserPresent, false);
                    //rebootTaskBuilder.SetTrigger(trigger);
                    //BackgroundTaskRegistration taskReg3 = rebootTaskBuilder.Register();
                    //String taskRegName = taskReg.Name;
                    //taskReg.Progress += OnBgTaskProgress;
                    System.Diagnostics.Debug.WriteLine("[RegisterTask] Background task registration is completed.");
                    taskRegistered = true;
                }
            }
        }



    }


}
