using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RubberJoins.Data;
using RubberJoins.Models;

namespace RubberJoins.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly RubberJoinsRepository _repository;

        public TodayViewModel ViewModel { get; set; } = new();

        public IndexModel(RubberJoinsRepository repository)
        {
            _repository = repository;
        }

        public async Task OnGetAsync()
        {
            string userId = User.Identity?.Name ?? "default";
            string todayDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

            try
            {
                // Get user settings to determine phase from start date
                var settings = await _repository.GetUserSettingsAsync(userId);
                var phaseInfo = CalculatePhaseAndWeek(settings?.StartDate);

                // Get day type and name
                var dayOfWeek = DateTime.UtcNow.DayOfWeek;
                var dayType = GetDayType(dayOfWeek);
                var dayName = dayOfWeek.ToString();

                // Get session type and minutes based on day type
                (string sessionType, int estMinutes, string location) = GetSessionDetails(dayType);

                // Get session steps for this day and phase
                var allSessionSteps = await _repository.GetSessionStepsAsync(dayType);
                var disabledToolIds = (settings?.DisabledTools ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var sessionSteps = FilterSessionStepsByPhase(allSessionSteps, phaseInfo.phase, disabledToolIds);

                // Get exercises for these steps
                var allExercises = await _repository.GetAllExercisesAsync();
                var exerciseMap = allExercises.ToDictionary(e => e.Id);

                // Get daily checks for today
                var dailyChecks = await _repository.GetDailyChecksAsync(userId, todayDate);
                var checkMap = dailyChecks.ToDictionary(c => $"{c.ItemType}:{c.ItemId}:{c.StepIndex}", c => c.Checked);

                // Build today steps
                var todaySteps = new List<TodayStep>();
                for (int i = 0; i < sessionSteps.Count; i++)
                {
                    var step = sessionSteps[i];
                    if (exerciseMap.TryGetValue(step.ExerciseId, out var exercise))
                    {
                        string rx = phaseInfo.phase == 1 ? step.Phase1Rx : step.Phase2Rx;
                        rx = rx ?? "";

                        string checkKey = $"step:{step.Id}:{i}";
                        bool isChecked = checkMap.TryGetValue(checkKey, out var val) && val;

                        todaySteps.Add(new TodayStep
                        {
                            Index = i,
                            Exercise = exercise,
                            Rx = rx,
                            Section = step.Section,
                            Checked = isChecked
                        });
                    }
                }

                // Get supplements grouped by time
                var allSupplements = await _repository.GetSupplementsAsync();
                var supplementChecks = new List<SupplementCheck>();
                foreach (var supp in allSupplements)
                {
                    string checkKey = $"supplement:{supp.Id}:0";
                    bool isChecked = checkMap.TryGetValue(checkKey, out var val) && val;
                    supplementChecks.Add(new SupplementCheck
                    {
                        Supplement = supp,
                        Checked = isChecked
                    });
                }

                ViewModel = new TodayViewModel
                {
                    Week = phaseInfo.week,
                    Phase = phaseInfo.phase,
                    DayName = dayName,
                    SessionType = sessionType,
                    DayKey = dayType,
                    EstMinutes = estMinutes,
                    Steps = todaySteps,
                    Supplements = supplementChecks
                };
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = "Unable to connect to the database. Some features may be unavailable.";
            }
        }

        private (int week, int phase) CalculatePhaseAndWeek(string? startDateStr)
        {
            if (string.IsNullOrEmpty(startDateStr) || !DateTime.TryParse(startDateStr, out var startDate))
            {
                return (1, 1);
            }

            var today = DateTime.UtcNow;
            int daysSinceStart = (today - startDate).Days;

            // 12-week program, 6 weeks per phase
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

        private (string sessionType, int estMinutes, string location) GetSessionDetails(string dayType)
        {
            return dayType switch
            {
                "gym" => ("Gym Session", 60, "Gym"),
                "home" => ("Home Session", 40, "Home"),
                "recovery" => ("Recovery", 30, "Home"),
                "rest" => ("Rest Day", 0, "Rest"),
                _ => ("Unknown", 0, "")
            };
        }

        private List<SessionStep> FilterSessionStepsByPhase(List<SessionStep> steps, int phase, HashSet<string> disabledToolIds)
        {
            return steps
                .Where(s =>
                    (s.PhaseOnly == null || s.PhaseOnly == phase) &&
                    !disabledToolIds.Contains(s.ExerciseId))
                .OrderBy(s => s.SortOrder)
                .ToList();
        }
    }
}
