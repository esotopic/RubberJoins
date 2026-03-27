using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RubberJoins.Data;
using RubberJoins.Models;

namespace RubberJoins.Pages
{
    [Authorize]
    public class WeekModel : PageModel
    {
        private readonly RubberJoinsRepository _repository;

        public List<WeekDayData> Days { get; set; } = new();
        public int Week { get; set; } = 1;
        public int Phase { get; set; } = 1;
        public string? ErrorMessage { get; set; }

        public WeekModel(RubberJoinsRepository repository)
        {
            _repository = repository;
        }

        public async Task OnGetAsync()
        {
            string userId = User.Identity?.Name ?? "default";

            try
            {
                var settings = await _repository.GetUserSettingsAsync(userId);
                var phaseInfo = CalculatePhaseAndWeek(settings?.StartDate);
                Week = phaseInfo.week;
                Phase = phaseInfo.phase;

                var allExercises = await _repository.GetAllExercisesAsync();
                var exerciseMap = allExercises.ToDictionary(e => e.Id);
                var allSupplements = await _repository.GetSupplementsAsync();
                var disabledToolIds = (settings?.DisabledTools ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

                // Get Monday of this week
                var today = DateTime.UtcNow;
                var dayOfWeek = today.DayOfWeek;
                var monday = today.AddDays(-(int)dayOfWeek + (int)DayOfWeek.Monday);
                if (dayOfWeek == DayOfWeek.Sunday) monday = monday.AddDays(-7);

                string[] dayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

                for (int i = 0; i < 7; i++)
                {
                    var date = monday.AddDays(i);
                    var dateStr = date.ToString("yyyy-MM-dd");
                    var isToday = date.Date == today.Date;
                    var isFuture = date.Date > today.Date;
                    var dayType = GetDayType(date.DayOfWeek);
                    var sessionLabel = GetSessionLabel(dayType);
                    var estMinutes = GetEstMinutes(dayType);

                    var dayData = new WeekDayData
                    {
                        DayName = dayNames[i],
                        DateLabel = date.ToString("MMM d"),
                        DayType = dayType,
                        SessionLabel = sessionLabel,
                        EstMinutes = estMinutes,
                        IsToday = isToday,
                        IsFuture = isFuture,
                        Categories = new List<CategoryProgress>()
                    };

                    if (!isFuture)
                    {
                        // Get session steps for this day type
                        var sessionSteps = await _repository.GetSessionStepsAsync(dayType);
                        var filteredSteps = sessionSteps
                            .Where(s => (s.PhaseOnly == null || s.PhaseOnly == Phase) && !disabledToolIds.Contains(s.ExerciseId))
                            .OrderBy(s => s.SortOrder)
                            .ToList();

                        // Get daily checks for this date
                        var dailyChecks = await _repository.GetDailyChecksAsync(userId, dateStr);
                        var checkMap = dailyChecks.ToDictionary(c => $"{c.ItemType}:{c.ItemId}:{c.StepIndex}", c => c.Checked);

                        // Calculate per-category progress
                        var catStats = new Dictionary<string, (int total, int done)>();
                        for (int j = 0; j < filteredSteps.Count; j++)
                        {
                            var step = filteredSteps[j];
                            if (!exerciseMap.TryGetValue(step.ExerciseId, out var exercise)) continue;
                            var cat = exercise.Category;

                            if (!catStats.ContainsKey(cat)) catStats[cat] = (0, 0);
                            var curr = catStats[cat];
                            curr.total++;

                            string checkKey = $"step:{step.Id}:{j}";
                            if (checkMap.TryGetValue(checkKey, out var isChecked) && isChecked)
                                curr.done++;

                            catStats[cat] = curr;
                        }

                        // Supplement progress
                        var suppChecks = dailyChecks.Where(c => c.ItemType == "supplement" && c.Checked).Count();
                        var suppTotal = allSupplements.Count;

                        // Build category list in order
                        var catOrder = new[] {
                            ("warmup_tool", "Warm-up", "#ff9500"),
                            ("mobility", "Mobility", "#34c759"),
                            ("strength", "Strength", "#af52de"),
                            ("recovery_tool", "Recovery", "#4a6cf7"),
                        };

                        foreach (var (catKey, label, color) in catOrder)
                        {
                            if (catStats.TryGetValue(catKey, out var stat) && stat.total > 0)
                            {
                                dayData.Categories.Add(new CategoryProgress
                                {
                                    Label = label,
                                    Color = color,
                                    Done = stat.done,
                                    Total = stat.total
                                });
                            }
                        }

                        // Add vitamins
                        if (suppTotal > 0)
                        {
                            dayData.Categories.Add(new CategoryProgress
                            {
                                Label = "Vitamins",
                                Color = "#ffcc00",
                                Done = suppChecks,
                                Total = suppTotal
                            });
                        }
                    }

                    Days.Add(dayData);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Unable to load weekly data. Some features may be unavailable.";
            }
        }

        private (int week, int phase) CalculatePhaseAndWeek(string? startDateStr)
        {
            if (string.IsNullOrEmpty(startDateStr) || !DateTime.TryParse(startDateStr, out var startDate))
                return (1, 1);
            var today = DateTime.UtcNow;
            int daysSinceStart = (today - startDate).Days;
            int week = Math.Min(daysSinceStart / 7 + 1, 12);
            int phase = week <= 6 ? 1 : 2;
            return (week, phase);
        }

        private string GetDayType(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => "gym",
                DayOfWeek.Tuesday => "home",
                DayOfWeek.Wednesday => "gym",
                DayOfWeek.Thursday => "home",
                DayOfWeek.Friday => "gym",
                DayOfWeek.Saturday => "recovery",
                DayOfWeek.Sunday => "rest",
                _ => "rest"
            };
        }

        private string GetSessionLabel(string dayType)
        {
            return dayType switch
            {
                "gym" => "Full Gym Session",
                "home" => "Home Mobility + Recovery",
                "recovery" => "Active Recovery",
                "rest" => "Rest + Passive Recovery",
                _ => ""
            };
        }

        private int GetEstMinutes(string dayType)
        {
            return dayType switch
            {
                "gym" => 85,
                "home" => 55,
                "recovery" => 60,
                "rest" => 40,
                _ => 0
            };
        }
    }

    public class WeekDayData
    {
        public string DayName { get; set; } = "";
        public string DateLabel { get; set; } = "";
        public string DayType { get; set; } = "";
        public string SessionLabel { get; set; } = "";
        public int EstMinutes { get; set; }
        public bool IsToday { get; set; }
        public bool IsFuture { get; set; }
        public List<CategoryProgress> Categories { get; set; } = new();
    }

    public class CategoryProgress
    {
        public string Label { get; set; } = "";
        public string Color { get; set; } = "";
        public int Done { get; set; }
        public int Total { get; set; }
        public int Percent => Total > 0 ? (int)Math.Round((double)Done / Total * 100) : 0;
    }
}
