using System;
using System.Runtime.Remoting.Messaging;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace RedisStore
{
    public class UniqueConstraintAttribute : Attribute
    {
        internal static LuaScript Script = LuaScript.Prepare(@"
local currentVal = redis.call('hget', @hashKey, @hashField)
if currentVal == @finalVal then
    return 1
end

if redis.call('sadd', @indexKey, @finalVal) == 1 then
    redis.call('hset', @hashKey, @hashField, @finalVal)
    redis.call('srem', @indexKey, currentVal)
    return 1
else
    return 0
end
");

        internal static void SetUniqueVal(RedisKey hashKey, RedisValue hashField, RedisValue value, RedisKey indexKey)
        {
            var result = Store.Database.ScriptEvaluate(Script, new {hashKey, hashField, finalVal = value, indexKey});
            if ((bool)result)
            {
                return;
            }

            throw new UniqueConstraintViolatedException();
        }

        internal static Task SetUniqueValAsync(RedisKey hashKey, RedisValue hashField, RedisValue value, RedisKey indexKey)
        {
            return Store.Database
                .ScriptEvaluateAsync(Script, new {hashKey, hashField, finalVal = value, indexKey})
                .ContinueWith(r =>
                {
                    if ((bool) r.Result)
                    {
                        return;
                    }

                    throw new UniqueConstraintViolatedException();
                });
        }
    }

    public class UniqueConstraintViolatedException : Exception
    {

    }
}