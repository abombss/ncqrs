﻿using System;
using System.Linq;
using Ncqrs.Eventing.Mapping;
using Ncqrs.Eventing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ncqs.EventingTests.MappingTests
{
    [TestClass]
    public class EventHandlerFactoryTest
    {
        private class EventFooMock : IEvent
        {
        }

        private class EventBarMock : IEvent
        {
        }

        private class MappedEventSourceMock : MappedEventSource
        {
            public Boolean EventHandlerFooExecuted = false;
            public Boolean EventHandlerBarExecuted = false;

            [EventHandler]
            public void EventMockEventHandler(EventFooMock evnt)
            {
                EventHandlerFooExecuted = true;
            }

            [EventHandler]
            public void EventMockEventHandler(EventBarMock evnt)
            {
                EventHandlerBarExecuted = true;
            }
        }

        private class IlligalStaticMethodMappedEventSourceMock : MappedEventSource
        {
            [EventHandler]
            public static void IlligalEventHandler(EventFooMock evnt)
            {
                
            }
        }

        private class IlligalEventHandlerWithMultipleArgumentsEventSourceMock : MappedEventSource
        {
            [EventHandler]
            public static void IlligalEventHandler(EventFooMock evnt)
            {

            }
        }

        [TestMethod]
        public void FactoryShouldFindMappedMethods()
        {
            var factory = new EventHandlerFactory();
            var source = new MappedEventSourceMock();
            
            var result = factory.CreateHandlers(source);

            Assert.AreEqual(result.Count(), 2);

            var firstResult = result.First();
            Assert.AreEqual(firstResult.Key, typeof(EventFooMock));
            Assert.IsInstanceOfType(firstResult.Value, typeof(ActionBasedEventHandler));

            var secondResult = result.Skip(1).First();
            Assert.AreEqual(secondResult.Key, typeof(EventBarMock));
            Assert.IsInstanceOfType(secondResult.Value, typeof(ActionBasedEventHandler));
        }

        [TestMethod]
        public void InvokingHandlerShouldCallMappedMethod()
        {
            var factory = new EventHandlerFactory();
            var source = new MappedEventSourceMock();

            var result = factory.CreateHandlers(source);

            Assert.AreEqual(result.Count(), 2);

            var firstResult = result.First();
            firstResult.Value.Invoke(new EventFooMock());

            Assert.IsTrue(source.EventHandlerFooExecuted);

            var secondResult = result.Skip(1).First();
            secondResult.Value.Invoke(new EventBarMock());
            Assert.IsTrue(source.EventHandlerBarExecuted);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void InvokingHandlerWithInvalidEventTypeShouldGiveException()
        {
            var factory = new EventHandlerFactory();
            var source = new MappedEventSourceMock();

            var result = factory.CreateHandlers(source);

            Assert.AreEqual(result.Count(), 2);

            var firstResult = result.First();
            var wrongEvent = new EventBarMock(); // EventFooMock is expected.
            firstResult.Value.Invoke(wrongEvent);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void StaticMethodsShouldNotBeMarkedAsEventHandler()
        {
            var factory = new EventHandlerFactory();
            var source = new IlligalStaticMethodMappedEventSourceMock();

            factory.CreateHandlers(source);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void EventHandlerMethodShouldNotHaveMoreThanOneParameter()
        {
            var factory = new EventHandlerFactory();
            var source = new IlligalEventHandlerWithMultipleArgumentsEventSourceMock();

            factory.CreateHandlers(source);
        }
    }
}
