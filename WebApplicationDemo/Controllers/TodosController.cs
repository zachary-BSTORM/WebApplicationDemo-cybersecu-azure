using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplicationDemo.Data;

namespace WebApplicationDemo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TodosController(AppDbContext db) : ControllerBase
    {
        [HttpGet]
        public async Task<IEnumerable<Todo>> GetAll() =>
            await db.Todos.AsNoTracking().ToListAsync();

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Todo>> GetById(int id)
        {
            var todo = await db.Todos.FindAsync(id);
            return todo is null ? NotFound() : todo;
        }

        [HttpPost]
        public async Task<ActionResult<Todo>> Create(Todo todo)
        {
            todo.Id = 0;
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = todo.Id }, todo);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, Todo input)
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null) return NotFound();

            todo.Title = input.Title;
            todo.IsDone = input.IsDone;
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null) return NotFound();

            db.Todos.Remove(todo);
            await db.SaveChangesAsync();
            return NoContent();
        }
    }
}
