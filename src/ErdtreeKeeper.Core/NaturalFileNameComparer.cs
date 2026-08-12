namespace ErdtreeKeeper.Core;

/// <summary>
/// Сравнение имён файлов так, как их читает человек: числа сравниваются как
/// числа, а не посимвольно.
///
/// Без этого "#12" встаёт перед "#2", а имена снимков почти всегда содержат
/// номера - привычка осталась от сбора пар "до/после".
///
/// Сравнение намеренно порядковое, а не по локали. Оно должно давать один и
/// тот же порядок в приложении, в тестах и в CI: у собранного exe включён
/// InvariantGlobalization, и правила локали там уже не те, что на машине
/// разработчика. Заодно латиница оказывается перед кириллицей - как в
/// проводнике, откуда у игрока и берётся ожидание.
/// </summary>
public sealed class NaturalFileNameComparer : IComparer<string>
{
    public static readonly NaturalFileNameComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var start1 = i;
                var start2 = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                var number1 = x.AsSpan(start1, i - start1).TrimStart('0');
                var number2 = y.AsSpan(start2, j - start2).TrimStart('0');

                // Более длинное число всегда больше - так сравнение не упирается
                // в разрядность и работает для любых номеров.
                if (number1.Length != number2.Length)
                    return number1.Length - number2.Length;

                var digits = number1.SequenceCompareTo(number2);
                if (digits != 0) return digits;

                continue;
            }

            var a = char.ToUpperInvariant(x[i]);
            var b = char.ToUpperInvariant(y[j]);
            if (a != b) return a - b;

            i++;
            j++;
        }

        return (x.Length - i) - (y.Length - j);
    }
}
