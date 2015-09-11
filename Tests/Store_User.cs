using System.Threading.Tasks;
using StackExchange.Redis;

namespace Tests
{
    public class Store_User : IUser
    {
        private readonly IDatabase _redis;

        public Store_User(IDatabase redis)
        {
            _redis = redis;
            _NameGetTask = _redis.StringGetAsync($"/IUser/{Id}/Name").ContinueWith(val => { return Name = val.ToString(); });
        }

        public int Id { get; set; }

        private Task<string> _NameGetTask;
        private string _Name;
        public string Name
        {
            get { return _NameGetTask.IsCompleted ? _Name : _NameGetTask.Result; }
            set { _redis.StringSet($"/IUser/{Id}/Name", value); }
        }

        public bool IsAwesome { get; set; }
        public bool? IsAwesomer { get; set; }
        public long Score { get; set; }
        public long? Pointses { get; set; }
    }
}