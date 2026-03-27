using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RubberJoins.Data;
using RubberJoins.Models;

namespace RubberJoins.Pages
{
    [Authorize]
    public class ProgressModel : PageModel
    {
        private readonly RubberJoinsRepository _repository;

        public ProgressViewModel ViewModel { get; set; } = new();

        public ProgressModel(RubberJoinsRepository repository)
        {
            _repository = repository;
        }

        public async Task OnGetAsync()
        {
            string userId = User.Identity?.Name ?? "default";
            string todayDate = DateTime.UtcNow.ToString("yyyy-MM-dd");

            try
            {
                // Get active enrollment for week/phase calculation
                var enrollment = await _repository.GetActiveEnrollmentAsync(userId);
                int week = 1;
                int phase = 1;
                if (enrollment != null && DateTime.TryParse(enrollment.StartDate, out var enrollStart))
                {
                    int daysSince = (DateTime.UtcNow - enrollStart).Days;
                    week = Math.Max(1, daysSince / 7 + 1);
                    phase = week <= 2 ? 1 : 2;
                }

                // Get session logs for stats
                var allSessionLogs = await _repository.GetSessionLogsAsync(userId);

                // Sessions this week
                var weekStart = DateTime.UtcNow.AddDays(-(int)DateTime.UtcNow.DayOfWeek);
                var sessionsThisWeek = allSessionLogs.Count(log =>
                    DateTime.TryParse(log.Date, out var logDate) &&
                    logDate >= weekStart);

                // Total sessions
                var sessionsTotal = allSessionLogs.Count;

                // Today's steps — use UserDailyPlan instead of SessionSteps
                var todayChecks = await _repository.GetDailyChecksAsync(userId, todayDate);
                var planEntries = await _repository.GetUserDailyPlanAsync(userId, todayDate);
                var settings = await _repository.GetUserSettingsAsync(userId);
                var disabledTools = (settings?.DisabledTools ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var filteredEntries = planEntries.Where(e => !disabledTools.Contains(e.ExerciseId)).ToList();

                int todayStepsDone = todayChecks.Count(c => c.ItemType == "step" && c.Checked);
                int todayStepsTotal = filteredEntries.Count;

                // Today's supplements
                var allSupplements = await _repository.GetSupplementsAsync();
                var suppCheckMap = todayChecks
                    .Where(c => c.ItemType == "supplement")
                    .ToDictionary(c => c.ItemId, c => c.Checked);

                int todaySuppsDone = allSupplements.Count(s => suppCheckMap.ContainsKey(s.Id) && suppCheckMap[s.Id]);
                int todaySuppsTotal = allSupplements.Count;

                // Get milestones (per-user)
                var milestones = await _repository.GetUserMilestonesAsync(userId);

                ViewModel = new ProgressViewModel
                {
                    Week = week,
                    Phase = phase,
                    SessionsThisWeek = sessionsThisWeek,
                    SessionsTotal = sessionsTotal,
                    TodayStepsDone = todayStepsDone,
                    TodayStepsTotal = todayStepsTotal > 0 ? todayStepsTotal : 0,
                    TodaySuppsDone = todaySuppsDone,
                    TodaySuppsTotal = todaySuppsTotal,
                    Milestones = milestones,
                    TodayLogged = true
                };
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = $"DB Error: {ex.GetType().Name}: {ex.Message}";
            }
        }
    }
}
