using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RedisLazyCache.Sample.Client.Models;

namespace RedisLazyCache.Sample.Client.Controllers
{
    [Route("api/[controller]")]
    public class BookController : Controller
    {

        private readonly ICacheService _cacheService;

        public BookController(ICacheService cacheService)
        {
            _cacheService = cacheService;
        }

        [HttpGet]
        public async Task<IEnumerable<BookModel>> GetAsync()
        {
            var list = await _cacheService.GetOrCreateAsync("booklist", async () =>
            {
                return await Task.Run(() =>
                {
                    var data = new List<BookModel>
                    {
                        new BookModel {DateAdded = DateTime.UtcNow, Title = "Book1"},
                        new BookModel {DateAdded = DateTime.UtcNow, Title = "Book2"},
                    };
                    Thread.Sleep(1000);
                    return data;
                });
            }, 10);
            return list; 
        }


      
    }
}
