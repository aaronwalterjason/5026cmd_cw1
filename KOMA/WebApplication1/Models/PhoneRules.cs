namespace KOMA.Models;

// One phone number rule for every form (bookings, profile, staff registration).
// Accepts what people actually type, with spaces or dashes between digits:
//   Malaysian numbers starting with 0, 9 to 11 digits in total   e.g. 012-345 6789, 011-1234 5678, 03-1234 5678
//   International numbers starting with +, 8 to 15 digits         e.g. +60 12-345 6789, +65 9123 4567
// Letters, brackets, extensions and separators at the start or end are refused.
public static class PhoneRules
{
    public const string Pattern = @"^(?:\+\d(?:[ -]?\d){7,14}|0\d(?:[ -]?\d){7,9})$";
    public const string Message = "Enter a valid phone number, e.g. 012-345 6789 or +60 12-345 6789.";
}
