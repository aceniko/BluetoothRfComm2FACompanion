using Bluetooth.Proximity.Connector.Processor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Core;

namespace Bluetooth.Proximity.Connector.Socket
{
    public class SocketService
    {
        public bool FlowCompleted = false;
        private StreamSocket streamSocket = null;
        
        private DataWriter dataWriter = null;
        private DataReader dataReader = null;


        private RfcommDeviceService _rfCommDeviceService = null;
        private InputMessageProcessor inputMessageProcessor;

        public SocketService() {
            
            lock (this) {
                streamSocket = new StreamSocket();
                inputMessageProcessor = new InputMessageProcessor();
            }
            
            
        }

        public async Task<bool> ConnectAsync(RfcommDeviceService rfcommDeviceService, bool startReceive) {
            try
            {
                
                    _rfCommDeviceService = rfcommDeviceService;
                    Debug.WriteLine("StreamSocket connectAsync");
                    await streamSocket.ConnectAsync(_rfCommDeviceService.ConnectionHostName, _rfCommDeviceService.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);
                    Debug.WriteLine("StreamSocket Connected!!!");
                    dataReader = new DataReader(streamSocket.InputStream);
                    dataWriter = new DataWriter(streamSocket.OutputStream);
                    Debug.WriteLine("DataReader/Writer initiated.");
                    if (startReceive)
                    {
                        ReceiveStringLoopAsync();
                    }

                return true;
                
            }catch(Exception e){
                Debug.WriteLine("Error: " + e.Message);
                return false;
                
            }
                
        }


        public async void ReceiveStringLoopAsync() {
            try
            {
                Debug.WriteLine("ReceiveStringLoopAsync Started.");
                    // Make sure device name isn't blank
                    StringBuilder strBuilder = new StringBuilder();
                    while (!FlowCompleted)
                    {

                        uint buf;
                        buf = await dataReader.LoadAsync(1);
                        if (dataReader.UnconsumedBufferLength > 0)
                        {
                            string s = dataReader.ReadString(1);
                            strBuilder.Append(s);
                            if (s.Equals("\n") || s.Equals("\r"))
                            {
                                try
                                {
                                    //ConversationList.Items.Add("Received: " + strBuilder.ToString());
                                    inputMessageProcessor.ProcessInputMessageAsync(strBuilder.ToString(), this);
                                    strBuilder.Clear();
                                }
                                catch (Exception exc)
                                {


                                }
                            }
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(0));
                        }
                    }
                await streamSocket.CancelIOAsync();
                dataReader.DetachStream();
                dataReader.Dispose();
                dataReader.DetachBuffer();
                dataWriter.Dispose();
                Debug.WriteLine("Finishind While Loop");
            }
            catch (Exception ex)
            {
                lock (this)
                {
                    if (streamSocket == null)
                    {
                        // Do not print anything here -  the user closed the socket.
                        if ((uint)ex.HResult == 0x80072745)
                        {
                            //rootPage.NotifyUser("Disconnect triggered by remote device", NotifyType.StatusMessage);
                        }

                        else if ((uint)ex.HResult == 0x800703E3) {
                            //rootPage.NotifyUser("The I/O operation has been aborted because of either a thread exit or an application request.", NotifyType.StatusMessage);
                        }
                        
                    }
                    else
                    {
                        //Disconnect("Read stream failed with error: " + ex.Message);
                    }
                }
            }
        }

        public async void SendMessage(string message)
        {
            try
            {
                if (!string.IsNullOrEmpty(message))
                {
                    var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(message);
                    string messageb64 = System.Convert.ToBase64String(plainTextBytes);

                    dataWriter.WriteString(messageb64);

                    await dataWriter.StoreAsync();

                }
            }
            catch (Exception ex) when ((uint)ex.HResult == 0x80072745)
            {
                // The remote device has disconnected the connection
                //rootPage.NotifyUser("Remote side disconnect: " + ex.HResult.ToString() + " - " + ex.Message, NotifyType.StatusMessage);
            }
        }
    }
}
