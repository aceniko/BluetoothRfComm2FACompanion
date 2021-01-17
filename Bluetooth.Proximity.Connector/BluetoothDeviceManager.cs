using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;

namespace Bluetooth.Proximity.Connector
{
    public class BluetoothDeviceManager
    {
        private BluetoothDevice bluetoothDevice { get; set; }
        private DeviceAccessStatus deviceAccessStatus;
        public BluetoothDeviceManager() { }

        public async Task<BluetoothDevice> GetBluetoothDeviceById(string id) {
            bluetoothDevice = await BluetoothDevice.FromIdAsync(id);
            // If we were unable to get a valid Bluetooth device object,
            // it's most likely because the user has specified that all unpaired devices
            // should not be interacted with.
            if (bluetoothDevice == null) {
                throw new Exception("Bluetooth Device returned null. Access Status = " + deviceAccessStatus.ToString());
            }
            return bluetoothDevice;
        }

        private async Task<RfcommDeviceServicesResult> GetRfCommServicesAsync(Guid serviceUuid) {
            return await bluetoothDevice.GetRfcommServicesForIdAsync(
                    RfcommServiceId.FromUuid(serviceUuid), BluetoothCacheMode.Uncached);
        }

        public async Task<RfcommDeviceService> GetRfCommDeviceService(Guid serviceUuid) {
            
            var sc = await bluetoothDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);

            int count = sc.Services.Count;

            var services = await bluetoothDevice.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(serviceUuid), BluetoothCacheMode.Uncached);

            if (services.Services.Count > 0)
            {
                return services.Services[0];
            }

            throw new Exception("Could not discover the service on the remote device");
        }

        public void CheckConsent(string id) {
            deviceAccessStatus = DeviceAccessInformation.CreateFromId(id).CurrentStatus;
            if (deviceAccessStatus == DeviceAccessStatus.DeniedByUser)
            {
                throw new Exception("This app does not have access to connect to the remote device (please grant access in Settings > Privacy > Other Devices");
            }
        }

        public async Task<DeviceAccessStatus> RequestAccess() {
            return await bluetoothDevice.RequestAccessAsync();
        }

        public BluetoothDevice GetBluetoothDevice() {
            return bluetoothDevice;
        }
        
    }
}
