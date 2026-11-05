using System;
using System.Collections.Generic;

namespace SpaceSim.Academy
{
    public enum InputDomain
    {
        /// <summary>&gt; 0</summary>
        Positive,
        /// <summary>≥ 0</summary>
        NonNegative,
        /// <summary>any finite number</summary>
        Any
    }

    /// <summary>One calculator field. The player types values in <see cref="Unit"/>; <see cref="Scale"/> converts them to SI.</summary>
    public sealed class CalculatorInput
    {
        public CalculatorInput(string id, string label, string symbol, string unit, double scale, double defaultValue,
            InputDomain domain = InputDomain.Positive)
        {
            Id = id;
            Label = label;
            Symbol = symbol;
            Unit = unit ?? "";
            Scale = scale;
            DefaultValue = defaultValue;
            Domain = domain;
        }

        public string Id { get; }
        public string Label { get; }
        public string Symbol { get; }
        /// <summary>Display unit, e.g. "т", "км", "кН".</summary>
        public string Unit { get; }
        /// <summary>SI value of one display unit (т → 1000 kg).</summary>
        public double Scale { get; }
        /// <summary>Default in display units.</summary>
        public double DefaultValue { get; }
        public InputDomain Domain { get; }

        public double ToSI(double displayValue) => displayValue * Scale;
    }

    /// <summary>A computed value. <see cref="Value"/> is SI; the UI shows <see cref="DisplayValue"/> in <see cref="Unit"/>.</summary>
    public sealed class CalculatorOutput
    {
        public CalculatorOutput(string label, string symbol, double value, string unit, double scale = 1.0)
        {
            Label = label;
            Symbol = symbol;
            Value = value;
            Unit = unit ?? "";
            Scale = scale;
        }

        public string Label { get; }
        public string Symbol { get; }
        public double Value { get; }
        public string Unit { get; }
        public double Scale { get; }
        public double DisplayValue => Value / Scale;
        public string DisplayText => AcademyNumber.Format(DisplayValue) + (Unit.Length > 0 ? " " + Unit : "");
    }

    /// <summary>Named set of input values (display units), e.g. "my rocket, stage 1" or "orbit of Quoris".</summary>
    public sealed class CalculatorPreset
    {
        public CalculatorPreset(string name, params double[] values)
        {
            Name = name;
            Values = values;
        }

        public string Name { get; }
        public double[] Values { get; }
    }

    public sealed class CalculatorResult
    {
        public string Error { get; private set; }
        public bool Ok => Error == null;
        public readonly List<CalculatorOutput> Outputs = new List<CalculatorOutput>();
        /// <summary>The formulas with the player's numbers substituted, in order.</summary>
        public readonly List<string> Steps = new List<string>();
        /// <summary>Consequences the player should know about (TWR &lt; 1, unrealistic mass ratio, …).</summary>
        public readonly List<string> Warnings = new List<string>();
        /// <summary>What the result does not include (Level A vs Level B), context.</summary>
        public readonly List<string> Notes = new List<string>();

        public static CalculatorResult Fail(string error) => new CalculatorResult { Error = error };

        public CalculatorOutput Find(string symbol)
        {
            foreach (var o in Outputs)
                if (o.Symbol == symbol) return o;
            return null;
        }

        internal CalculatorResult Out(string label, string symbol, double value, string unit, double scale = 1.0)
        {
            Outputs.Add(new CalculatorOutput(label, symbol, value, unit, scale));
            return this;
        }

        internal CalculatorResult Step(string text)
        {
            Steps.Add(text);
            return this;
        }

        internal CalculatorResult Warn(string text)
        {
            Warnings.Add(text);
            return this;
        }

        internal CalculatorResult Note(string text)
        {
            Notes.Add(text);
            return this;
        }
    }

    /// <summary>
    /// An interactive Level A calculator. Its formula is a call into the central physics layer
    /// (<c>RocketEquations</c>, <c>OrbitalMechanics</c>, <c>TransferCalculator</c>, <c>AtmosphericDrag</c>, …) — never a copy.
    /// </summary>
    public sealed class AcademyCalculator
    {
        readonly CalculatorInput[] inputs;
        readonly Func<double[], CalculatorResult> compute;

        public AcademyCalculator(string id, string title, string topicId, string description, CalculatorInput[] inputs,
            Func<double[], CalculatorResult> compute)
        {
            Id = id;
            Title = title;
            TopicId = topicId;
            Description = description;
            this.inputs = inputs ?? new CalculatorInput[0];
            this.compute = compute ?? throw new ArgumentNullException(nameof(compute));
        }

        public string Id { get; }
        public string Title { get; }
        /// <summary>The Academy topic that explains this calculator's formula.</summary>
        public string TopicId { get; }
        public string Description { get; }
        public IReadOnlyList<CalculatorInput> Inputs => inputs;
        public List<CalculatorPreset> Presets { get; } = new List<CalculatorPreset>();

        /// <summary>Default input values (display units).</summary>
        public double[] Defaults()
        {
            var d = new double[inputs.Length];
            for (int i = 0; i < d.Length; i++) d[i] = inputs[i].DefaultValue;
            return d;
        }

        public int IndexOf(string inputId)
        {
            for (int i = 0; i < inputs.Length; i++)
                if (inputs[i].Id == inputId) return i;
            return -1;
        }

        /// <summary>Validates the player's values (display units), converts them to SI and runs the formula.</summary>
        public CalculatorResult Compute(IReadOnlyList<double> displayValues)
        {
            if (displayValues == null || displayValues.Count != inputs.Length)
                return CalculatorResult.Fail("Нужно " + inputs.Length + " значений.");
            var si = new double[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                CalculatorInput input = inputs[i];
                double v = displayValues[i];
                string name = "«" + input.Label + "» (" + input.Symbol + ")";
                if (double.IsNaN(v) || double.IsInfinity(v)) return CalculatorResult.Fail(name + ": введите число.");
                if (input.Domain == InputDomain.Positive && !(v > 0.0)) return CalculatorResult.Fail(name + " должно быть больше нуля.");
                if (input.Domain == InputDomain.NonNegative && v < 0.0) return CalculatorResult.Fail(name + " не может быть отрицательным.");
                si[i] = input.ToSI(v);
            }
            try
            {
                return compute(si) ?? CalculatorResult.Fail("Расчёт не дал результата.");
            }
            catch (ArgumentException e)
            {
                return CalculatorResult.Fail("Расчёт невозможен: " + e.Message);
            }
        }
    }
}
