using System.Timers;

namespace Discord_RaceBot
{
    //CountdownTimer inherits Timer and allows us to pass the race information to our ElapsedEventHandler
    class CountdownTimer : Timer
    {
        public RaceItem race;
    }
}
