using System.ComponentModel.DataAnnotations;

namespace KOMA.Models
{
    // Owned by Engineer B. Staff duty schedule (Admin > Accounts > Staff > Schedule).
    // The list of shifts is data, not code: names, times and how many people each needs are edited in Settings.
    public class Shift
    {
        public int Id { get; set; }

        [Required, StringLength(30)]
        public string Name { get; set; } = "";       // e.g. "Morning"

        public TimeOnly Starts { get; set; }
        public TimeOnly Ends { get; set; }           // earlier than Starts = runs past midnight

        [Range(0, 50)]
        public int Needed { get; set; } = 2;         // people needed; fewer marks the day with a red dot

        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;   // retired shifts stay for old schedules

        public double Hours
        {
            get
            {
                var h = (Ends.ToTimeSpan() - Starts.ToTimeSpan()).TotalHours;
                return h <= 0 ? h + 24 : h;
            }
        }
    }

    // One person on one day. No row = Off.
    public class ShiftAssignment
    {
        public int Id { get; set; }

        [Required, StringLength(450)]
        public string UserId { get; set; } = "";

        public DateOnly Date { get; set; }

        public int? ShiftId { get; set; }            // null when on leave
        public Shift? Shift { get; set; }

        public bool IsLeave { get; set; }
    }

    // Draft / published state of one week (Monday). Edits set ChangedAt; Publish sets PublishedAt.
    public class ScheduleWeek
    {
        [Key]
        public DateOnly WeekStart { get; set; }
        public DateTime? ChangedAt { get; set; }
        public DateTime? PublishedAt { get; set; }

        [StringLength(100)]
        public string? PublishedBy { get; set; }
    }
}
