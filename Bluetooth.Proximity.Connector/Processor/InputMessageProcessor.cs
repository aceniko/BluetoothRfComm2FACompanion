using Bluetooth.Proximity.BusinessObjects.Constants;
using Bluetooth.Proximity.BusinessObjects.TransportModels;
using Bluetooth.Proximity.Connector.Socket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Authentication.Identity.Provider;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;
using Windows.UI.Popups;

namespace Bluetooth.Proximity.Connector.Processor
{
    public class InputMessageProcessor
    {
        private SecondaryAuthenticationFactorAuthenticationResult authResult = null;

        public async void ProcessInputMessageAsync(string message, SocketService socketService)
        {

            Debug.WriteLine("ProcessInputMessageAsync: " + message);
            string decodedJsonObject = Encoding.UTF8.GetString(System.Convert.FromBase64String(message));

            dynamic obj = JsonConvert.DeserializeObject(decodedJsonObject);

            string response_command = obj.response_command;

            switch (response_command)
            {
                case Commands.REQUEST_KEYS_REPLY:
                    Debug.WriteLine("REQUEST_KEYS_REPLY");
                    string deviceId = System.Guid.NewGuid().ToString();
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
                    //rootPage.AddDeviceInRegisteredDeviceList(deviceId);

                    System.Diagnostics.Debug.WriteLine("Device Registration is Complete!");

                    IReadOnlyList<SecondaryAuthenticationFactorInfo> deviceList = await SecondaryAuthenticationFactorRegistration.FindAllRegisteredDeviceInfoAsync(
                        SecondaryAuthenticationFactorDeviceFindScope.User);

                    //RefreshDeviceList(deviceList);
                    break;
                case Commands.REQUEST_NOUNCE_REPLY:
                    Debug.WriteLine("REQUEST_NOUNCE_REPLY");
                    Debug.WriteLine(decodedJsonObject);
                    var model = JsonConvert.DeserializeObject<NonceResponse>(decodedJsonObject);
                    byte[] nonceByteArray = System.Convert.FromBase64String(model.Nonce);

                    IBuffer nonceBuffer = CryptographicBuffer.CreateFromByteArray(nonceByteArray);

                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    string selectedDevice = localSettings.Values["SelectedDevice"] as String;

                    authResult = await SecondaryAuthenticationFactorAuthentication.StartAuthenticationAsync(selectedDevice, nonceBuffer);
                    if (authResult.Status != SecondaryAuthenticationFactorAuthenticationStatus.Started)
                    {
                        //ShowToastNotification("Unexpected! Could not start authentication!");
                        throw new Exception("Unexpected! Could not start authentication! Status: " + authResult.Status);
                    }
                    
                    byte[] devNonce = { 0 };
                    byte[] svcHmac = { 0 };
                    byte[] sessNonce = { 0 };

                    CryptographicBuffer.CopyToByteArray(authResult.Authentication.ServiceAuthenticationHmac, out svcHmac);
                    CryptographicBuffer.CopyToByteArray(authResult.Authentication.SessionNonce, out sessNonce);
                    CryptographicBuffer.CopyToByteArray(authResult.Authentication.DeviceNonce, out devNonce);


                    HMacRequest requestMacMessage = new HMacRequest {
                        RequestCommand = Commands.REQUEST_MAC,
                        DevNonce = System.Convert.ToBase64String(devNonce),
                        SessNonce = System.Convert.ToBase64String(sessNonce),
                        SrvNonce = System.Convert.ToBase64String(svcHmac)
                    }; 

                    socketService.SendMessage(JsonConvert.SerializeObject(requestMacMessage));

                    break;
                case Commands.REQUEST_MAC_REPLY:
                    Debug.WriteLine("REQUEST_MAC_REPLY");
                    Debug.WriteLine(decodedJsonObject);
                    var macResponseModel = JsonConvert.DeserializeObject<HMacResponse>(decodedJsonObject);

                    var hmacDkByteArray = System.Convert.FromBase64String(macResponseModel.HmacDk);
                    var hmacSkByteArray = System.Convert.FromBase64String(macResponseModel.HmacSk);

                    IBuffer hmacDkByteBuffer = CryptographicBuffer.CreateFromByteArray(hmacDkByteArray);
                    IBuffer hmacSkByteBuffer = CryptographicBuffer.CreateFromByteArray(hmacSkByteArray);


                    if (authResult != null) {
                        Debug.WriteLine("Finishing authentication");

                        #region manual

                        /*
                        byte[] comb;
                        CryptographicBuffer.CopyToByteArray(authResult.Authentication.DeviceConfigurationData, out comb);

                        byte[] dka = new byte[32];
                        byte[] aka = new byte[32];
                        for (int index = 0; index < dka.Length; index++)
                        {
                            dka[index] = comb[index];
                        }
                        for (int index = 0; index < aka.Length; index++)
                        {
                            aka[index] = comb[dka.Length + index];
                        }
                        // Create device key and authentication key
                        IBuffer dk = CryptographicBuffer.CreateFromByteArray(dka);
                        IBuffer ak = CryptographicBuffer.CreateFromByteArray(aka);

                        // Calculate the HMAC
                        MacAlgorithmProvider hMACSha256Provider = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha256);

                        CryptographicKey deviceHmacKey = hMACSha256Provider.CreateKey(dk);
                        IBuffer deviceHmac = CryptographicEngine.Sign(deviceHmacKey, authResult.Authentication.DeviceNonce);

                        // sessionHmac = HMAC(authKey, deviceHmac || sessionNonce)
                        IBuffer sessionHmac;
                        byte[] deviceHmacArray = { 0 };
                        CryptographicBuffer.CopyToByteArray(deviceHmac, out deviceHmacArray);

                        Debug.WriteLine("Nonce HMAC: " + System.Convert.ToBase64String(deviceHmacArray));

                        byte[] sessionNonceArray = { 0 };
                        CryptographicBuffer.CopyToByteArray(authResult.Authentication.SessionNonce, out sessionNonceArray);

                        combinedDataArray = new byte[deviceHmacArray.Length + sessionNonceArray.Length];
                        for (int index = 0; index < deviceHmacArray.Length; index++)
                        {
                            combinedDataArray[index] = deviceHmacArray[index];
                        }
                        for (int index = 0; index < sessionNonceArray.Length; index++)
                        {
                            combinedDataArray[deviceHmacArray.Length + index] = sessionNonceArray[index];
                        }

                        // Get a Ibuffer from combinedDataArray
                        IBuffer sessionMessage = CryptographicBuffer.CreateFromByteArray(combinedDataArray);

                        // Calculate sessionHmac
                        CryptographicKey authHmacKey = hMACSha256Provider.CreateKey(ak);
                        sessionHmac = CryptographicEngine.Sign(authHmacKey, sessionMessage);


                        byte[] sessionHmacArray = { 0 };
                            CryptographicBuffer.CopyToByteArray(sessionHmac, out sessionHmacArray);

                        Debug.WriteLine("Device HMAC :" + macResponseModel.HmacDk);
                        Debug.WriteLine("APP HMAC :" + System.Convert.ToBase64String(deviceHmacArray));

                        Debug.WriteLine("Device Session HMAC:" + macResponseModel.HmacSk);
                        Debug.WriteLine("APP Session HMAC:" + System.Convert.ToBase64String(sessionHmacArray));

                        SecondaryAuthenticationFactorFinishAuthenticationStatus authStatus = await authResult.Authentication.FinishAuthenticationAsync(deviceHmac,
                            sessionHmac);
                        */
                        #endregion


                        SecondaryAuthenticationFactorFinishAuthenticationStatus authStatus = await authResult.Authentication.FinishAuthenticationAsync(hmacDkByteBuffer, hmacSkByteBuffer);

                        Debug.WriteLine("Authentication Status: " + authStatus.ToString());

                        if (authStatus != SecondaryAuthenticationFactorFinishAuthenticationStatus.Completed)
                        {
                            //ShowToastNotification("Unable to complete authentication!");
                            System.Diagnostics.Debug.WriteLine("Unable to complete authentication");
                            throw new Exception("Unable to complete authentication!");
                        }

                        socketService.FlowCompleted = true;
                        
                    }
                    else {
                        Debug.WriteLine("AuthResult is NULL");
                    }

                    
                    

                    break;
            }


        }
    }
}
