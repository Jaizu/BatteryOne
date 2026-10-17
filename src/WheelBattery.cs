using System;

namespace BatteryOne
{
    public enum WheelState { Connected, WheelOff, DongleNotFound, Error }

    public sealed class WheelBattery
    {
        public WheelState State { get; private set; }
        public int Percent { get; private set; }
        public string Status { get; private set; }
        public string Health { get; private set; }
        public TimeSpan? Remaining { get; private set; }
        public string Error { get; private set; }
        public DateTime Time { get; private set; } = DateTime.Now;

        public bool IsConnected => State == WheelState.Connected;
        public bool IsCharging => Status == "Charging";

        const ushort VendorId = 0x30B7; // Heusinkveld
        const ushort DongleId = 0x1008; // One Steering Wheel USB dongle

        static readonly string[] StatusNames = { "Unknown", "Charging", "Discharging", "Not charging", "Full" };
        static readonly string[] HealthNames =
        {
            "Unknown", "Good", "Overheat", "Overvoltage", "Unspecified failure", "Cold",
            "Watchdog timer expired", "Safety timer expired", "Calibration required", "Warm", "Cool", "Hot"
        };

        /// <summary>
        /// Asks the dongle for the wheel's battery info, using the same message as SmartControl.
        /// Request: report 5 (device TX), group 0x20 (steering wheel) | 0x80 (request flag), parameter 3 (battery info).
        /// Reply:   report 6 (device RX), group 0x20, parameter 3, then
        ///          [3] USB cable status, [4] percentage, [5..8] remaining seconds, [9] status, [10] health.
        /// </summary>
        public static WheelBattery Read()
        {
            try
            {
                var result = Hid.Query(VendorId, DongleId, new byte[] { 5, 0xA0, 3 }, new byte[] { 6, 0x20, 3 }, 2000, out byte[] r);
                if (result == HidQueryResult.DeviceNotFound) return new WheelBattery { State = WheelState.DongleNotFound };
                if (result == HidQueryResult.NoResponse || r.Length < 11) return new WheelBattery { State = WheelState.WheelOff };

                uint seconds = BitConverter.ToUInt32(r, 5);
                return new WheelBattery
                {
                    State = WheelState.Connected,
                    Percent = Math.Min(100, (int)r[4]),
                    // The wheel sends 0xFFFFFFFF when it does not know the remaining time
                    Remaining = seconds > 0 && seconds < 1000 * 3600 ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null,
                    Status = r[9] < StatusNames.Length ? StatusNames[r[9]] : "Unknown",
                    Health = r[10] < HealthNames.Length ? HealthNames[r[10]] : "Unknown",
                };
            }
            catch (Exception e)
            {
                return new WheelBattery { State = WheelState.Error, Error = e.Message };
            }
        }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case WheelState.Connected: return "Connected";
                    case WheelState.WheelOff: return "Not connected";
                    case WheelState.DongleNotFound: return "Not connected";
                    default: return "Error";
                }
            }
        }

        public string DetailText
        {
            get
            {
                switch (State)
                {
                    case WheelState.Connected:
                        string text = Status;
                        if (Remaining.HasValue) text += $" · {(int)Remaining.Value.TotalHours}h {Remaining.Value.Minutes:00}m left";
                        return text + $" · Health: {Health}";
                    case WheelState.WheelOff: return "The wheel is off or out of range";
                    case WheelState.DongleNotFound: return "USB dongle not found";
                    default: return Error;
                }
            }
        }

        public string TrayText
        {
            get
            {
                string text = IsConnected ? $"BatteryOne: {Percent}% ({Status})" : $"BatteryOne: OFF ({DetailText})";
                return text.Length > 63 ? text.Substring(0, 63) : text;
            }
        }
    }
}
