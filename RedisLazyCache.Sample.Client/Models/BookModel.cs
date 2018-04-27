using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RedisLazyCache.Sample.Client.Models
{
    public class BookModel
    {
        public DateTime DateAdded { get; set; }
        public string Title { get; set; }
    }
}
