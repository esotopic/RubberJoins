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
                // Get user settings for phase calculation
                var settings = await _repository.GetUserSettingsAsync(userId);
                var phaseInfo = CalculatePhaseAndWeek(settings?.StartDate);

                // Get session logs for stats
                var allSessionLogs = await _repository.GetSessionLogsAsync(userId);

                // Sessions this week
                var weekStart = DateTime.UtcNow.AddDays(-(int)DateTime.UtcNow.DayOfWeek);
                var sessionsThisWeek = allSessionLogs.Count(log =>
                    DateTime.TryParse(log.Date, out var logDate) &&
                    logDate >= weekStart);

                // Total sessions
                var sessionsTotal = allSessionLogs.Count;

                // Today's steps
                var todayLog = await _repository.GetSessionLogForDateAsync(userId, todayDate);
                int todayStepsDone = todayLog?.StepsDone ?? 0;
                int todayStepsTotal = todayLog?.StepsTotal ?? 0;

                // Today's supplements
                var allSupplements = await _repository.GetSupplementsAsync();
                var todayChecks = await _repository.GetDailyChecksAsync(userId, todayDate);
                var suppCheckMap = todayChecks
                    .Where(c => c.ItemType == "supplement")
                    .ToDictionary(c => c.ItemId, c => c.Checked);

                int todaySuppsDone = allSupplements.Count(s => suppCheckMap.ContainsKey(s.Id) && suppCheckMap[s.Id]);
                int todaySuppsTotal = allSupplements.Count;

                // Get milestones (per-user)
                var milestones = await _repository.GetUserMilestonesAsync(userId);

                // Check if today is logged
                bool todayLogged = todayLog != null && todayLog.StepsTotal > 0;

                ViewModel = new ProgressViewModel
                {
                    Week = phaseInfo.week,
                    Phase = phaseInfo.phase,
                    SessionsThisWeek = sessionsThisWeek,
                    SessionsTotal = sessionsTotal,
                    TodayStepsDone = todayStepsDone,
                    TodayStepsTotal = todayStepsTotal > 0 ? todayStepsTotal : 0,
                    TodaySuppsDone = todaySuppsDone,
                    TodaySuppsTotal = todaySuppsTotal,
                    Milestones = milestones,
                    TodayLogged = todayLogged
                };
            }
            catch (Exception ex)
            {
                ViewModel.ErrorMessage = "Unable to connect to the database. Progress data may be unavailable.";
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
    }
}
