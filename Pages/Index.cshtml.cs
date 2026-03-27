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
                // Check enrollment
                var enrollment = await _repository.GetActiveEnrollmentAsync(userId);
                if (enrollment == null)
                {
                    ViewModel.ErrorMessage = "NO_ENROLLMENT";
                    return;
                }

                // Calculate week from enrollment start date
                int week = 1;
                if (DateTime.TryParse(enrollment.StartDate, out var enrollStart))
                {
                    int daysSince = (DateTime.UtcNow - enrollStart).Days;
                    week = Math.Max(1, daysSince / 7 + 1);
                }

                // Get today's plan
                var planEntries = await _repository.GetUserDailyPlanAsync(userId, todayDate);

                // Get user settings for disabled tools
                var settings = await _repository.GetUserSettingsAsync(userId);
                var disabledToolIds = (settings?.DisabledTools ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).ToHashSet();

                // Filter out disabled tools
                planEntries = planEntries.Where(e => !disabledToolIds.Contains(e.ExerciseId)).ToList();

                string dayType = planEntries.Count > 0 ? planEntries[0].DayType : "rest";
                var dayOfWeek = DateTime.UtcNow.DayOfWeek;
                var dayName = dayOfWeek.ToString();

                (string sessionType, int estMinutes, string location) = GetSessionDetails(dayType);

                // Get exercises
                var allExercises = await _repository.GetAllExercisesAsync();
                var exerciseMap = allExercises.ToDictionary(e => e.Id);

                // Get daily checks
                var dailyChecks = await _repository.GetDailyChecksAsync(userId, todayDate);
                var checkMap = dailyChecks.ToDictionary(c => $"{c.ItemType}:{c.ItemId}:{c.StepIndex}", c => c.Checked);

                // Build today steps from plan entries
                var todaySteps = new List<TodayStep>();
                for (int i = 0; i < planEntries.Count; i++)
                {
                    var entry = planEntries[i];
                    if (exerciseMap.TryGetValue(entry.ExerciseId, out var exercise))
                    {
                        string checkKey = $"step:{entry.Id}:{i}";
                        bool isChecked = checkMap.TryGetValue(checkKey, out var val) && val;

                        todaySteps.Add(new TodayStep
                        {
                            Index = i,
                            SessionStepId = entry.Id,
                            Exercise = exercise,
                            Rx = entry.Rx ?? "",
                            Section = entry.Category,
                            Checked = isChecked
                        });
                    }
                }

                // Get supplements
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

                int durationWeeks = 4; // RubberJoins is 4 weeks
                ViewModel = new TodayViewModel
                {
                    Week = week,
                    Phase = week <= 2 ? 1 : 2, // simplified phase calc for 4-week program
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
    }
}
