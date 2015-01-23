﻿using System;
using System.Linq;
using System.Threading.Tasks;

using Orleans;

namespace Orleankka
{
    public class TestActor : Actor, ITestActor
    {
        string fooText = "";
        string barText = "";

        readonly IActorObserverCollection actorObservers;

        public TestActor()
        {
            actorObservers = new ActorObserverCollection(()=> Path);
        }

        public override Task OnTell(object message)
        {
            return this.Handle((dynamic)message);
        }

        public override async Task<object> OnAsk(object message)
        {
            return await this.Answer((dynamic)message);
        }

        public Task Handle(DoFoo cmd)
        {
            fooText = cmd.Text;
            return TaskDone.Done;
        }

        public Task Handle(DoBar cmd)
        {
            barText = cmd.Text;
            return TaskDone.Done;
        }

        public Task Handle(Throw cmd)
        {
            throw cmd.Exception;
        }

        public Task<string> Answer(GetFoo query)
        {
            return Task.FromResult(fooText + "-" + Id);
        }

        public Task<string> Answer(GetBar query)
        {
            return Task.FromResult(barText + "-" + Id);
        }

        public Task Handle(PublishFoo cmd)
        {
            actorObservers.Notify(new FooPublished {Foo = cmd.Foo});
            return TaskDone.Done;
        }

        public Task Handle(PublishBar cmd)
        {
            actorObservers.Notify(new BarPublished {Bar = cmd.Bar});
            return TaskDone.Done;
        }

        public Task Handle(Attach cmd)
        {
            actorObservers.Add(cmd.Observer);
            return TaskDone.Done;
        }

        public Task Handle(Detach cmd)
        {
            actorObservers.Remove(cmd.Observer);
            return TaskDone.Done;
        }
    }
}
