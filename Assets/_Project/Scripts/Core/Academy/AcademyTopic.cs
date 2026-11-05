using System;
using System.Collections.Generic;

namespace SpaceSim.Academy
{
    /// <summary>One symbol of a formula: what it means and its SI unit.</summary>
    public readonly struct FormulaVariable
    {
        public FormulaVariable(string symbol, string meaning, string unit)
        {
            Symbol = symbol;
            Meaning = meaning;
            Unit = unit;
        }

        public string Symbol { get; }
        public string Meaning { get; }
        public string Unit { get; }
    }

    public sealed class AcademyFormula
    {
        public AcademyFormula(string expression, params FormulaVariable[] variables)
        {
            Expression = expression;
            Variables = variables ?? new FormulaVariable[0];
        }

        public string Expression { get; }
        public IReadOnlyList<FormulaVariable> Variables { get; }
    }

    /// <summary>
    /// A Flight Academy lesson. Every formula has its variables and units, a worked example computed live from the
    /// player's planet and rocket, its practical use in the game and its limitations.
    /// </summary>
    public sealed class AcademyTopic
    {
        public string Id = "";
        public int Number;
        public string Title = "";
        public readonly List<AcademyFormula> Formulas = new List<AcademyFormula>();
        public string Explanation = "";
        /// <summary>Worked example with the current planet's / rocket's numbers.</summary>
        public string Example = "";
        /// <summary>Where the player meets this in the game.</summary>
        public string Application = "";
        /// <summary>What the formula leaves out (Level A vs Level B).</summary>
        public string Limitations = "";
        /// <summary>Calculators that use this topic's formulas.</summary>
        public readonly List<string> CalculatorIds = new List<string>();
        public AcademyExercise Exercise;
    }

    public enum AnswerVerdict
    {
        /// <summary>Within the tolerance (1 % by default).</summary>
        Correct,
        /// <summary>Within 5 %: right method, rounding or a slightly different constant.</summary>
        Close,
        Wrong,
        /// <summary>Not a number.</summary>
        Invalid
    }

    public readonly struct ExerciseCheck
    {
        public ExerciseCheck(AnswerVerdict verdict, string message)
        {
            Verdict = verdict;
            Message = message;
        }

        public AnswerVerdict Verdict { get; }
        public string Message { get; }
        public bool Solved => Verdict == AnswerVerdict.Correct;
    }

    /// <summary>
    /// A practice problem with a numeric answer computed by the central physics functions. Hints and the worked solution are
    /// revealed one by one (the answer is never shown up front); wrong answers get a diagnosis of the likely mistake.
    /// </summary>
    public sealed class AcademyExercise
    {
        public string Id = "";
        public string Question = "";
        /// <summary>Correct answer in <see cref="Unit"/> (display units).</summary>
        public double Answer;
        public string Unit = "";
        public double RelativeTolerance = 0.01;
        public readonly List<string> Hints = new List<string>();
        /// <summary>Worked solution, one step at a time; the last step contains the answer.</summary>
        public readonly List<string> SolutionSteps = new List<string>();

        public ExerciseCheck Check(string text) =>
            AcademyNumber.TryParse(text, out double v) ? Check(v) : new ExerciseCheck(AnswerVerdict.Invalid, "Введите число, например 7788.5 или 3.2e4.");

        public ExerciseCheck Check(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return new ExerciseCheck(AnswerVerdict.Invalid, "Введите конечное число.");
            double expected = Answer;
            double error = expected != 0.0 ? Math.Abs(value - expected) / Math.Abs(expected) : Math.Abs(value);
            if (error <= RelativeTolerance) return new ExerciseCheck(AnswerVerdict.Correct, "Верно!");
            if (error <= 0.05)
                return new ExerciseCheck(AnswerVerdict.Close, "Почти: метод верный, но расхождение " + (error * 100.0).ToString("0.0") +
                                                               " %. Проверьте округления и константы (g₀ = 9.80665, G = 6.6743·10⁻¹¹).");
            return new ExerciseCheck(AnswerVerdict.Wrong, Diagnose(value, expected));
        }

        /// <summary>Recognises the classic mistakes from the ratio of the answer to the expected value.</summary>
        static string Diagnose(double value, double expected)
        {
            if (expected != 0.0 && value != 0.0)
            {
                if (Math.Sign(value) != Math.Sign(expected)) return "Неверно: знак не тот. Проверьте, что из чего вычитается.";
                double k = value / expected;
                if (Near(k, 1000.0) || Near(k, 0.001)) return "Неверно: ответ отличается в 1000 раз — похоже, перепутаны километры и метры (или тонны и килограммы).";
                if (Near(k, 1.0e6) || Near(k, 1.0e-6)) return "Неверно: ответ отличается в миллион раз — проверьте перевод км → м: в квадрате (км² → м²) это 10⁶.";
                if (Near(k, RocketEquationsG0) || Near(k, 1.0 / RocketEquationsG0)) return "Неверно: отличие ровно в g₀ ≈ 9.81 раза — не забыт ли множитель g₀ (или он лишний)?";
                if (Near(k, Math.Sqrt(2.0)) || Near(k, 1.0 / Math.Sqrt(2.0))) return "Неверно: отличие в √2 — перепутаны круговая скорость √(μ/r) и вторая космическая √(2μ/r)?";
                if (Near(k, 2.0) || Near(k, 0.5)) return "Неверно: ответ отличается в 2 раза — проверьте множитель ½ или сумму двух импульсов.";
                if (Near(k, 60.0) || Near(k, 1.0 / 60.0)) return "Неверно: отличие в 60 раз — секунды и минуты?";
                if (Near(k, 180.0 / Math.PI) || Near(k, Math.PI / 180.0)) return "Неверно: отличие в 57.3 раза — радианы и градусы?";
            }
            return "Неверно. Откройте подсказку — она не выдаёт ответ, а подсказывает следующий шаг.";
        }

        const double RocketEquationsG0 = SpaceSim.Flight.RocketEquations.G0;

        static bool Near(double k, double target) => Math.Abs(k - target) <= 0.03 * target;
    }
}
