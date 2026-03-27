using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RubberJoins.Data;
using RubberJoins.Models;

namespace RubberJoins.Pages
{
    [Authorize]
    public class LibraryModel : PageModel
    {
        private readonly RubberJoinsRepository _repository;

        public List<Exercise> Exercises { get; set; } = new();
        public string? ErrorMessage { get; set; }

        public LibraryModel(RubberJoinsRepository repository)
        {
            _repository = repository;
        }

        public async Task OnGetAsync()
        {
            try
            {
                Exercises = await _repository.GetAllExercisesAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = "Unable to connect to the database. Library may be unavailable.";
            }
        }
    }
}
