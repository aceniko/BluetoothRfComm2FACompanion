using Bluetooth.Proximity.BusinessObjects.Constants;
using Bluetooth.Proximity.BusinessObjects.TransportModels;
using Bluetooth.Proximity.Connector;
using Bluetooth.Proximity.Connector.DeviceWatcher;
using Bluetooth.Proximity.Connector.Socket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Core;
using Windows.Data.Xml.Dom;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Security.Authentication.Identity.Provider;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Notifications;

namespace BackgroundTasks
{
    public sealed class CDFTask : IBackgroundTask
    {
        private string _deviceId;


        ManualResetEvent _exitTaskEvent = null;

        private volatile SocketService socketService;
        private volatile BluetoothDeviceManager bluetoothDeviceManager;


        public async void Run(IBackgroundTaskInstance taskInstance)
        {

            Debug.WriteLine("RUN Start");
            var deferral = taskInstance.GetDeferral();

            _exitTaskEvent = new ManualResetEvent(false);

            taskInstance.Canceled += TaskInstanceCanceled;

            IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceInfoList = await SecondaryAuthenticationFactorRegistration.FindAllRegisteredDeviceInfoAsync(SecondaryAuthenticationFactorDeviceFindScope.AllUsers);

            if (deviceInfoList.Count == 0)
            {
                // Quit the task silently
                _exitTaskEvent.Set();
                return;
            }
            _deviceId = deviceInfoList[0].DeviceId;
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["SelectedDevice"] = _deviceId;
            localSettings.Values["SelectedDeviceName"] = deviceInfoList[0].DeviceFriendlyName;

            SecondaryAuthenticationFactorAuthentication.AuthenticationStageChanged += OnAuthenticationStageChanged;

            // Wait the task exit event
            _exitTaskEvent.WaitOne();

            deferral.Complete();

        }

        async void PerformAuthentication()
        {
            SecondaryAuthenticationFactorAuthenticationStageInfo authStageInfo = await SecondaryAuthenticationFactorAuthentication.GetAuthenticationStageInfoAsync();

            if (authStageInfo.Stage != SecondaryAuthenticationFactorAuthenticationStage.CollectingCredential)
            {
                Debug.WriteLine("Unexpected!");
                throw new Exception("Unexpected!");
            }



            Debug.WriteLine("Use first device '" + _deviceId + "' in the list to signin");
            //ShowToastNotification("Performing Auth!");
            if (bluetoothDeviceManager == null)
            {
                bluetoothDeviceManager = new BluetoothDeviceManager();
            }
            //Get the selected device from app settings

            //String m_selectedDeviceId = localSettings.Values["SelectedDevice"] as String;

            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));

            foreach (DeviceInformation device in devices)
            {
                Debug.WriteLine("Device name:" + device.Name);
                //if (device.Name == "HUAWEI P30 Pro")
                //{

                bluetoothDeviceManager.CheckConsent(device.Id);
                try
                {
                    await bluetoothDeviceManager.GetBluetoothDeviceById(device.Id);
                    var service = await bluetoothDeviceManager.GetRfCommDeviceService(Constants.RfcommServiceUuid);
                    var attributes = await service.GetSdpRawAttributesAsync();
                    if (!attributes.ContainsKey(Constants.SdpServiceNameAttributeId))
                    {
                        return;
                    }
                    var attributeReader = DataReader.FromBuffer(attributes[Constants.SdpServiceNameAttributeId]);
                    var attributeType = attributeReader.ReadByte();
                    if (attributeType != Constants.SdpServiceNameAttributeType)
                    {
                        return;
                    }
                    var serviceNameLength = attributeReader.ReadByte();

                    // The Service Name attribute requires UTF-8 encoding.
                    attributeReader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                    Debug.WriteLine("Check socket service");
                    //deviceWatcherService.StopWatcher();
                    if (socketService == null)
                    {
                        Debug.WriteLine("Socket service is NULL Create socket service");
                        socketService = new SocketService();
                    }
                    Debug.WriteLine("Connecting to socket...");
                    if (socketService.ConnectAsync(service, false).Result)
                    {
                        //ShowToastNotification("Start Authentication");
                        Debug.WriteLine("AuthenticateWithBluetoothDevice");
                        AuthenticateWithBluetoothDevice();
                    }


                }
                catch (Exception e)
                {
                    Debug.WriteLine("Error: " + e.Message);
                    return;

                }
                //}
            }

        }

        public static void ShowToastNotification(string message)
        {

            ToastTemplateType toastTemplate = ToastTemplateType.ToastImageAndText01;
            XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(toastTemplate);

            // Set Text
            XmlNodeList toastTextElements = toastXml.GetElementsByTagName("text");
            toastTextElements[0].AppendChild(toastXml.CreateTextNode(message));

            // Set image
            // Images must be less than 200 KB in size and smaller than 1024 x 1024 pixels.
            XmlNodeList toastImageAttributes = toastXml.GetElementsByTagName("image");
            ((XmlElement)toastImageAttributes[0]).SetAttribute("src", "ms-appx:///Images/logo-80px-80px.png");
            ((XmlElement)toastImageAttributes[0]).SetAttribute("alt", "logo");

            // toast duration
            IXmlNode toastNode = toastXml.SelectSingleNode("/toast");
            ((XmlElement)toastNode).SetAttribute("duration", "short");

            // toast navigation
            var toastNavigationUriString = "#/MainPage.xaml?param1=12345";
            var toastElement = ((XmlElement)toastXml.SelectSingleNode("/toast"));
            toastElement.SetAttribute("launch", toastNavigationUriString);

            // Create the toast notification based on the XML content you've specified.
            ToastNotification toast = new ToastNotification(toastXml);

            // Send your toast notification.
            ToastNotificationManager.CreateToastNotifier().Show(toast);

        }

        public async void OnAuthenticationStageChanged(object sender, SecondaryAuthenticationFactorAuthenticationStageChangedEventArgs args)
        {

            // The application should check the args.StageInfo.Stage to determine what to do in next. Note that args.StageInfo.Scenario will have the scenario information (SignIn vs CredentialPrompt).
            Debug.WriteLine("Authentication Stage = " + args.StageInfo.Stage.ToString());
            Debug.WriteLine("Scenario: " + args.StageInfo.Scenario.ToString());

            if (args.StageInfo.Stage == SecondaryAuthenticationFactorAuthenticationStage.WaitingForUserConfirmation)
            {
                //ShowToastNotification("Stage = WaitingForUserConfirmation");
                // This event is happening on a ThreadPool thread, so we need to dispatch to the UI thread.
                // Getting the dispatcher from the MainView works as long as we only have one view.
                String deviceName = Windows.Storage.ApplicationData.Current.LocalSettings.Values["SelectedDeviceName"] as String;
                await SecondaryAuthenticationFactorAuthentication.ShowNotificationMessageAsync(
                    deviceName,
                    SecondaryAuthenticationFactorAuthenticationMessage.LookingForDevice);
            }
            else if (args.StageInfo.Stage == SecondaryAuthenticationFactorAuthenticationStage.CollectingCredential)
            {
                //ShowToastNotification("Stage = CollectingCredential");

                PerformAuthentication();
            }
            else
            {
                if (args.StageInfo.Stage == SecondaryAuthenticationFactorAuthenticationStage.StoppingAuthentication)
                {
                    SecondaryAuthenticationFactorAuthentication.AuthenticationStageChanged -= OnAuthenticationStageChanged;
                    _exitTaskEvent.Set();
                }

                SecondaryAuthenticationFactorAuthenticationStage stage = args.StageInfo.Stage;
            }

        }

        private async void AuthenticateWithBluetoothDevice()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            String m_selectedDeviceId = localSettings.Values["SelectedDevice"] as String;



            IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceList = await SecondaryAuthenticationFactorRegistration.FindAllRegisteredDeviceInfoAsync(SecondaryAuthenticationFactorDeviceFindScope.AllUsers);
            if (deviceList.Count == 0)
            {
                //ShowToastNotification("Unexpected exception, device list = 0");
                throw new Exception("Unexpected exception, device list = 0");
            }
            NonceRequest message = new NonceRequest { RequestCommand = Commands.REQUEST_NOUNCE };
            socketService.SendMessage(JsonConvert.SerializeObject(message));


            socketService.ReceiveStringLoopAsync();



        }

        void TaskInstanceCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            Debug.WriteLine("TaskInstanceCanceled");
            _exitTaskEvent.Set();
        }
    }
}
