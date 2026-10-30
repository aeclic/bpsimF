using System;
using System.Collections.Generic;
using Bpsim.Parts.Circuits.Model;

namespace Bpsim.Parts.Circuits
{
    /// <summary>Persistent BPLE elements. Only topology changes allocate; stepping reuses solver buffers.</summary>
    public sealed class CircuitNetwork
    {
        public readonly List<CircuitDevice> Devices = new List<CircuitDevice>();
        private readonly List<CircuitElement> elements = new List<CircuitElement>();
        private readonly List<Link> links = new List<Link>();
        private readonly OptimizedCircuitSimulator solver = new OptimizedCircuitSimulator(0.02);
        private struct Link { public Electrode A, B; }
        public int ElementCount => elements.Count;
        public int NodeCount => solver.NodeCount;
        public int UnknownCount => solver.UnknownCount;

        public void Add(CircuitDevice device)
        {
            Devices.Add(device);
            elements.AddRange(device.Elements);
        }

        public void RefreshElements()
        {
            elements.Clear();
            for (int i = 0; i < Devices.Count; i++) elements.AddRange(Devices[i].Elements);
        }

        public void Connect(Electrode a, Electrode b)
        {
            if (a == null || b == null || a == b || a.IsConnected || b.IsConnected) return;
            CircuitFactory.Connect(a, b);
            links.Add(new Link { A = a, B = b });
        }

        public void DisconnectAll()
        {
            for (int i = 0; i < links.Count; i++) CircuitFactory.Disconnect(links[i].A, links[i].B);
            links.Clear();
            for (int i = 0; i < Devices.Count; i++) Devices[i].ResetTaps();
        }

        public void Clear()
        {
            DisconnectAll(); Devices.Clear(); elements.Clear(); solver.Clear();
        }

        public void Step(double deltaTime)
        {
            solver.SetDeltaTime(deltaTime);
            for (int i = 0; i < Devices.Count; i++) Devices[i].BeforeStep();
            solver.Simulate(elements);
            for (int i = 0; i < Devices.Count; i++) Devices[i].AfterStep(deltaTime);
        }
    }

    public sealed class CircuitDevice
    {
        public CircuitPart Data;
        public readonly int Rotation;
        public readonly bool Flipped;
        public readonly List<CircuitElement> Elements = new List<CircuitElement>(4);
        public readonly Electrode[] Ports = new Electrode[4];
        public readonly double[] Voltages = new double[4];
        public readonly double[] Currents = new double[4];
        public readonly bool[] Grounded = new bool[4];
        public bool Triggered { get; private set; }
        public Electrode WirelessPort { get; private set; }
        public Resistor WirelessResistance { get; private set; }
        private readonly List<Electrode> taps = new List<Electrode>();
        private int tapCount;
        private Resistor resistor;
        private VoltageSource source;
        private Capacitor capacitor;
        private Vcc[] outputs;
        private Switch transistor;
        private SPDTSwitch spdt;
        private Switch toggle;
        private int input1 = -1, input2 = -1, previousLevel = -1;
        private double[] delay;
        private int delayPosition;
        private double delayTime;
        private bool delayHigh;
        private double maxCurrent;
        private byte overloadedPorts;
        public byte DisconnectedPorts => Data.Invalid && (Data.Kind == CircuitKind.Wire || Data.Kind == CircuitKind.Interface || Data.Kind == CircuitKind.Capacitor || Data.Kind == CircuitKind.PowerSource)
            ? (Data.Kind == CircuitKind.Wire && (Data.Index == 1 || Data.Index == 3) && Rotation % 7 == 6 ? Data.BurnedPorts : Data.TerminalMask) : (byte)0;

        public CircuitDevice(CircuitPart data, int rotation = 0, bool flipped = false)
        {
            Data = data; Rotation = rotation; Flipped = flipped;
            Build();
        }

        private int WorldDir(int local) => (local + Rotation % 4 + 4) % 4;
        private T Add<T>(T element) where T : CircuitElement
        {
            Elements.Add(element); element.ElementUpdated += Observe; return element;
        }
        private void Port(int local, Electrode electrode) { Ports[WorldDir(local)] = electrode; }
        private void Probe(int local) { Port(local, Add(new Wire(1)).Electrodes[0]); }
        private void Driver(int local, double voltage = 0, double resistance = 0)
        {
            if (outputs == null) outputs = new Vcc[4];
            outputs[local] = Add(new Vcc(voltage, resistance)); Port(local, outputs[local].Electrode);
        }
        private void Build()
        {
            int index = Data.Index;
            switch (Data.Kind)
            {
                case CircuitKind.Wire:
                    if (index == 1 || index == 3)
                    {
                        int mode = ((Rotation % 7) + 7) % 7;
                        var x = Add(new Wire(2));
                        int a = mode <= 1 ? mode : mode <= 5 ? mode - 2 : 0;
                        int b = mode <= 1 ? a + 2 : mode <= 5 ? (mode + 1) % 4 : 2;
                        Ports[a] = x.Electrodes[0]; Ports[b] = x.Electrodes[1];
                        if (mode == 6)
                        {
                            var y = Add(new Wire(2)); Ports[1] = y.Electrodes[0]; Ports[3] = y.Electrodes[1];
                        }
                    }
                    else
                    {
                        var wire = Add(new Wire(4));
                        for (int i = 0; i < 4; i++) Port(i, wire.Electrodes[i]);
                    }
                    break;
                case CircuitKind.Switch:
                    if (index == 5)
                    {
                        spdt = Add(new SPDTSwitch()); Port(2, spdt.Pole); Port(1, spdt.Throw1); Port(3, spdt.Throw2);
                    }
                    else { toggle = Add(new Switch()); Port(2, toggle.Pole); Port(0, toggle.Throw); }
                    break;
                case CircuitKind.Resistor:
                case CircuitKind.Voltmeter:
                case CircuitKind.Ammeter:
                    if (Data.IsIndicator) Data.Mode = (byte)(Rotation >= 4 ? 1 : 0);
                    if (Data.Kind == CircuitKind.Voltmeter && Rotation >= 4)
                    { Probe(2); Probe(0); break; }
                    if (Data.Kind == CircuitKind.Ammeter && Rotation >= 4)
                    { source = Add(new VoltageSource(5, 100)); Port(2, source.Anode); Port(0, source.Cathode); break; }
                    resistor = Add(new Resistor(Data.ValueA));
                    Port(Data.IsIndicator ? 2 : 0, resistor.Electrode1); Port(Data.IsIndicator ? 0 : 2, resistor.Electrode2);
                    break;
                case CircuitKind.PowerSource:
                    source = Add(new VoltageSource(Data.ValueA, Data.ValueB)); Port(0, source.Anode); Port(2, source.Cathode);
                    break;
                case CircuitKind.Capacitor:
                    capacitor = Add(new Capacitor(Data.ValueA, Data.Charge, Data.ValueB));
                    Port(2, capacitor.Anode); Port(0, capacitor.Cathode); break;
                case CircuitKind.Ground: Port(1, Add(new Ground()).Electrode); break;
                case CircuitKind.Vcc: Driver(0); break;
                case CircuitKind.IntegratedCircuit: BuildChip(); break;
                case CircuitKind.Interface:
                    if (Rotation % 5 >= 3)
                    {
                        var a = Add(new Wire(2)); var b = Add(new Wire(2)); var r = Add(new Resistor(1e-5));
                        CircuitFactory.Connect(a.Electrodes[1], r.Electrode1);
                        CircuitFactory.Connect(b.Electrodes[1], r.Electrode2);
                        bool vertical = Rotation % 5 == 4;
                        Ports[vertical ? 3 : 2] = a.Electrodes[0]; Ports[vertical ? 1 : 0] = b.Electrodes[0];
                    }
                    else
                    {
                        var wire = Add(new Wire(4));
                        for (int i = 0; i < 4; i++)
                            if (Rotation % 5 == 0 || (i % 2 == (Rotation % 5 == 2 ? 1 : 0))) Ports[i] = wire.Electrodes[i];
                    }
                    break;
                case CircuitKind.PowerTransmitter:
                    if (index == 40) Port(3, Add(new Wire(1)).Electrodes[0]);
                    else
                    {
                        WirelessResistance = Add(new Resistor(0)); Port(1, WirelessResistance.Electrode1);
                        WirelessPort = WirelessResistance.Electrode2;
                    }
                    break;
                case CircuitKind.PartTrigger: Probe(2); input1 = WorldDir(2); break;
                case CircuitKind.PressureSensor:
                    if (Data.Mode == 0)
                    {
                        resistor = Add(new Resistor(10000)); Port(2, resistor.Electrode1); Port(0, resistor.Electrode2);
                    }
                    else { Driver(2); Driver(0); }
                    break;
                case CircuitKind.DigitalDisplay:
                    for (int i = 0; i < 4; i++) { if (Data.Mode == 0) Probe(i); else Driver(i, 0, 0.01); }
                    break;
            }
            byte mask = 0;
            for (int i = 0; i < 4; i++) if (Ports[i] != null) mask |= (byte)(1 << i);
            Data.TerminalMask = mask;
        }

        private void BuildChip()
        {
            int chip = Data.Index - 22;
            if (chip == 10 || chip == 11)
            {
                Probe(2); input1 = WorldDir(2); transistor = Add(new Switch());
                Port(1, transistor.Pole); Port(3, transistor.Throw); return;
            }
            if (chip == 12)
            {
                source = Add(new VoltageSource(0, 50000)); Port(2, source.Anode); Port(0, source.Cathode); return;
            }
            int a = (chip == 2 || chip == 4 || chip == 6 || chip == 8 || chip == 13) ? 1 : 2;
            int b = (chip >= 2 && chip <= 9) ? (chip % 2 == 0 ? 3 : 1) : chip == 13 ? 3 : -1;
            if (Flipped && b >= 0) { if (a == 1) a = 3; b = b == 1 ? 3 : 1; }
            Probe(a); input1 = WorldDir(a);
            if (b >= 0) { Probe(b); input2 = WorldDir(b); }
            Driver(0);
            if (chip == 14 || chip == 15) delay = new double[chip == 14 ? 5 : 25];
        }

        // Additional interface/wireless contacts join a Wire node without stealing its physical electrode.
        public Electrode Tap(int worldDirection)
        {
            var node = Ports[worldDirection]?.Element as Wire;
            if (node == null) return null;
            Electrode electrode;
            if (tapCount < taps.Count && taps[tapCount].Element == node) electrode = taps[tapCount];
            else
            {
                electrode = new Electrode(node, null);
                if (tapCount < taps.Count) taps[tapCount] = electrode; else taps.Add(electrode);
            }
            tapCount++; node.Electrodes.Add(electrode); return electrode;
        }
        internal void ResetTaps()
        {
            for (int i = 0; i < tapCount; i++) taps[i].Element.Electrodes.Remove(taps[i]);
            tapCount = 0;
        }

        private void Observe(CircuitSimulator simulator, SimulationResult result)
        {
            if (double.IsNaN(result.U) || double.IsNaN(result.I) || double.IsInfinity(result.U) || double.IsInfinity(result.I)) Data.SolveFailed = true;
            if (Math.Abs(result.I) > 100000)
                for (int i = 0; i < 4; i++) if (Ports[i] != null && Ports[i].Element == result.Element) overloadedPorts |= (byte)(1 << i);
            if (!double.IsNaN(result.I)) maxCurrent = Math.Max(maxCurrent, Math.Abs(result.I));
            for (int i = 0; i < 4; i++) if (Ports[i] == result.Electrode)
            {
                Voltages[i] = result.U; Currents[i] = result.I; Grounded[i] = result.IsGrounded;
            }
        }
        public static int Logic(double voltage, bool grounded)
        {
            if (!grounded || double.IsNaN(voltage) || voltage < -10 || voltage > 10) return -1;
            return voltage > 2.5 ? 1 : 0;
        }
        internal void BeforeStep()
        {
            Triggered = false;
            Data.SolveFailed = false;
            maxCurrent = 0;
            overloadedPorts = 0;
            for (int i = 0; i < 4; i++) { Voltages[i] = double.NaN; Currents[i] = 0; Grounded[i] = false; }
            if (toggle != null) toggle.Toggle(Data.Closed && !Data.Invalid);
            if (spdt != null) spdt.Toggle(Data.Closed);
            if (resistor != null && Data.Kind != CircuitKind.PressureSensor) resistor.Resistance = Math.Max(0, Data.ValueA);
            if (source != null && Data.Kind == CircuitKind.PowerSource)
            { source.Voltage = Data.ValueA; source.Resistance = Math.Max(0.05, Data.ValueB); }
            if (capacitor != null) { capacitor.Capacitance = Math.Max(1e-9, Data.ValueA); capacitor.Resistance = Math.Max(0, Data.ValueB); }
            if (Data.Kind == CircuitKind.Vcc) SetOutput(0, Data.Closed ? 5 : 0, Data.Closed ? 0.05 : 0);
            if (Data.Kind == CircuitKind.PressureSensor)
            {
                double p = Math.Max(0, Data.Pressure);
                if (resistor != null) resistor.Resistance = p > 0 ? Math.Min(500 / p, 10000) : 10000;
                else { SetOutput(2, -p * 0.1, p * 0.0005); SetOutput(0, p * 0.1, p * 0.0005); }
            }
            if (Data.Kind == CircuitKind.DigitalDisplay && Data.Mode != 0)
                for (int i = 0; i < 4; i++) SetOutput(i, (Data.DisplayValue & (1 << i)) != 0 ? 10 : 0, 0.01);
        }
        internal void AfterStep(double dt)
        {
            int first = -1, second = -1;
            // Keep signed two-terminal readings in LOCAL electrode order through every rotation.
            bool leftFirst = Data.Kind == CircuitKind.Capacitor || Data.IsIndicator;
            int a = WorldDir(leftFirst ? 2 : 0);
            int b = WorldDir(leftFirst ? 0 : 2);
            if (Ports[a] != null) first = a;
            if (Ports[b] != null) second = b;
            if (first < 0) for (int i = 0; i < 4; i++) if (Ports[i] != null) { first = i; break; }
            Data.Voltage1 = first < 0 || double.IsNaN(Voltages[first]) ? 0 : (float)Voltages[first];
            Data.Voltage2 = second < 0 || double.IsNaN(Voltages[second]) ? 0 : (float)Voltages[second];
            Data.Grounded1 = first >= 0 && Grounded[first]; Data.Grounded2 = second >= 0 && Grounded[second];
            Data.Current = first < 0 || double.IsNaN(Currents[first]) ? 0 : (float)Currents[first];
            if (capacitor != null)
            {
                Data.Charge = capacitor.Charge;
                double max = Data.Index == 15 ? 5 : Data.Index == 16 ? 50 : 500;
                if (Math.Abs(capacitor.Charge / capacitor.Capacitance) > max)
                {
                    Data.Invalid = true;
                    capacitor.Charge = Math.Sign(capacitor.Charge) * max * capacitor.Capacitance;
                    Data.Charge = capacitor.Charge;
                }
            }
            if ((Data.Kind == CircuitKind.Wire || Data.Kind == CircuitKind.Interface) && maxCurrent > 100000)
            { Data.Invalid = true; Data.BurnedPorts |= overloadedPorts; }
            if (Data.Kind == CircuitKind.PowerSource && maxCurrent > 10000) Data.Invalid = true;
            if (Data.Kind == CircuitKind.PartTrigger)
            {
                int next = Logic(Voltages[input1], Grounded[input1]);
                Triggered = previousLevel >= 0 && next >= 0 && previousLevel != next;
                previousLevel = next; Data.Invalid = next < 0;
            }
            if (Data.Kind == CircuitKind.DigitalDisplay && Data.Mode == 0)
            {
                int value = 0;
                for (int i = 0; i < 4; i++) if (Logic(Voltages[WorldDir(i)], Grounded[WorldDir(i)]) == 1) value |= 1 << i;
                Data.DisplayValue = value;
            }
            if (Data.Kind == CircuitKind.IntegratedCircuit) UpdateChip(dt);
        }
        private void SetOutput(int local, double voltage, double resistance)
        {
            if (outputs == null || outputs[local] == null) return;
            outputs[local].Potential = voltage; outputs[local].Resistance = resistance;
        }
        private void UpdateChip(double dt)
        {
            int chip = Data.Index - 22;
            int x = input1 < 0 ? -1 : Logic(Voltages[input1], Grounded[input1]);
            int y = input2 < 0 ? -1 : Logic(Voltages[input2], Grounded[input2]);
            if (transistor != null) { Data.Invalid = x < 0; transistor.Toggle(x == (chip == 10 ? 1 : 0)); return; }
            if (chip == 12) { UpdateDiode(); return; }
            Data.Invalid = x < 0 || (input2 >= 0 && y < 0);
            if (chip == 13)
            {
                // Opamp inputs are analog; grounded values outside digital logic range remain valid.
                Data.Invalid = !Grounded[input1] || !Grounded[input2] || double.IsNaN(Voltages[input1]) || double.IsNaN(Voltages[input2]);
                if (!Data.Invalid) SetOutput(0, Math.Max(-10, Math.Min(10, (Voltages[input1] - Voltages[input2]) * 1000)), 0.05);
                return;
            }
            bool high = delayHigh;
            if (delay != null)
            {
                delayTime += dt;
                while (delayTime >= 0.02 - 1e-9)
                {
                    high = delay[delayPosition] > 0;
                    delayHigh = high;
                    delay[delayPosition] = x == 1 ? 1 : 0;
                    delayPosition = (delayPosition + 1) % delay.Length; delayTime -= 0.02;
                }
            }
            else if (!Data.Invalid)
            {
                switch (chip)
                {
                    case 0: high = x == 1; break;
                    case 1: high = x == 0; break;
                    case 2: case 3: high = x == 1 || y == 1; break;
                    case 4: case 5: high = x == 0 && y == 0; break;
                    case 6: case 7: high = x == 1 && y == 1; break;
                    case 8: case 9: high = x == 0 || y == 0; break;
                }
            }
            SetOutput(0, high ? 5 : 0, high ? 0.05 : 0);
        }
        private void UpdateDiode()
        {
            int a = WorldDir(2), b = WorldDir(0);
            double current = Currents[a], voltage = Voltages[a] - Voltages[b];
            if (double.IsNaN(voltage) || double.IsNaN(current)) { source.Voltage = 0; source.Resistance = 50000; return; }
            if (current >= 10) voltage = Math.Log(current / 1e-6 + 1) * 0.05;
            else
            {
                double estimate = Math.Min(voltage, 1);
                for (int i = 0; i < 16; i++)
                {
                    double exp = Math.Exp(estimate / 0.05);
                    double change = ((1e-6 * (exp - 1) - current) * 2e-5 * exp + estimate - voltage)
                        / ((1e-6 * (2 * exp - 1) - current) * 0.0004 * exp + 1);
                    estimate -= change; if (Math.Abs(change) < 1e-5) break;
                }
                voltage = Math.Max(estimate, -0.15); current = 1e-6 * (Math.Exp(voltage / 0.05) - 1);
            }
            source.Resistance = 0.05 / (current + 1e-6); source.Voltage = voltage - current * source.Resistance;
        }
    }
}
