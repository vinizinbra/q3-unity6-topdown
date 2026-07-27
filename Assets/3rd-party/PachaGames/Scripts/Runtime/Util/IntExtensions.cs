
using System.Text;

public static class IntExtensions
{
	public static string ToOrdinal(this int number)
	{
		if (number <= 0)
		{
			return number.ToString();
		}

		int lastDigit = number % 10;
		int secondLastDigit = (number / 10) % 10;

		string suffix;

		if (secondLastDigit == 1)
		{
			suffix = "th";
		}
		else
		{
			switch (lastDigit)
			{
				case 1:
					suffix = "st";
					break;
				case 2:
					suffix = "nd";
					break;
				case 3:
					suffix = "rd";
					break;
				default:
					suffix = "th";
					break;
			}
		}

		return number.ToString() + suffix;
	}

	public static string ToOrdinalPlusOne(this int number)
	{
		number += 1;

		if (number <= 0)
		{
			return number.ToString();
		}

		int lastDigit = number % 10;
		int secondLastDigit = (number / 10) % 10;

		string suffix;

		if (secondLastDigit == 1)
		{
			suffix = "th";
		}
		else
		{
			switch (lastDigit)
			{
				case 1:
					suffix = "st";
					break;
				case 2:
					suffix = "nd";
					break;
				case 3:
					suffix = "rd";
					break;
				default:
					suffix = "th";
					break;
			}
		}

		// Use StringBuilder to minimize garbage
		var sb = new StringBuilder(6); // 6 is enough for most numbers + suffix
		sb.Append(number);
		sb.Append(suffix);

		return sb.ToString();
	}

	public static int PlusOne(this int number)
	{
		return number + 1;
	}public static string PlusOneString(this int number)
	{
		return $"{number.PlusOne()}";
	}

}
