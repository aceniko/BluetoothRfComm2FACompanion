using Bluetooth.Proximity.BusinessObjects.Device;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.UI.Core;

namespace Bluetooth.Proximity.Connector.DeviceWatcher
{
    public class DeviceWatcherService
    {
        private Windows.Devices.Enumeration.DeviceWatcher deviceWatcher = null;
        private readonly CoreDispatcher _coreDispatcher;

        public DeviceWatcherService(CoreDispatcher dispatcher) {
            _coreDispatcher = dispatcher;
            string[] requestedProperties = new string[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };

            deviceWatcher = DeviceInformation.CreateWatcher("(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")",
                                                            requestedProperties,
                                                            DeviceInformationKind.AssociationEndpoint);
        }


        public void AddedUIEvent(TypedEventHandler<Windows.Devices.Enumeration.DeviceWatcher, DeviceInformation> typedEvent) {
            deviceWatcher.Added += typedEvent;
        }

        public void UpdatedUIEvent(TypedEventHandler<Windows.Devices.Enumeration.DeviceWatcher, DeviceInformationUpdate> typedEvent) {
            deviceWatcher.Updated += typedEvent;
        }

        public void EnumerationCompletedUIEvent(TypedEventHandler<Windows.Devices.Enumeration.DeviceWatcher, Object> typedEvent) {
            deviceWatcher.EnumerationCompleted += typedEvent;
        }

        public void RemovedUIEvent(TypedEventHandler<Windows.Devices.Enumeration.DeviceWatcher, DeviceInformationUpdate> typedEvent) {
            deviceWatcher.Removed += typedEvent;
        }

        public void StoppedUIEvent(TypedEventHandler<Windows.Devices.Enumeration.DeviceWatcher, Object> typedEvent) {
            deviceWatcher.Stopped += typedEvent;
        }

        public void StartDeviceWatcherService() {

            deviceWatcher.Start();
        }

        public void StopWatcher()
        {
            if (null != deviceWatcher)
            {
                if ((DeviceWatcherStatus.Started == deviceWatcher.Status ||
                     DeviceWatcherStatus.EnumerationCompleted == deviceWatcher.Status))
                {
                    deviceWatcher.Stop();
                }
                deviceWatcher = null;
            }
        }

        public Windows.Devices.Enumeration.DeviceWatcher GetDeviceWatcher() {
            return deviceWatcher;
        }
    }
}
