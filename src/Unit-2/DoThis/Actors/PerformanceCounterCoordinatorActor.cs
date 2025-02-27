﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms.DataVisualization.Charting;
using Akka.Actor;

namespace ChartApp.Actors
{
    /// <summary>
    /// Actor responsible for translating UI calls into ActorSystem messages
    /// </summary>
    public class PerformanceCounterCoordinatorActor : ReceiveActor
    {
        #region Message types

        /// <summary>
        /// Subscribe the <see cref="ChartingActor"/> to 
        /// updates for <see cref="Counter"/>.
        /// </summary>
        public class Watch
        {
            public Watch(CounterType counter)
            {
                Counter = counter;
            }

            public CounterType Counter { get; private set; }
        }

        /// <summary>
        /// Unsubscribe the <see cref="ChartingActor"/> to 
        /// updates for <see cref="Counter"/>
        /// </summary>
        public class Unwatch
        {
            public Unwatch(CounterType counter)
            {
                Counter = counter;
            }

            public CounterType Counter { get; private set; }
        }

        #endregion

        /// <summary>
        /// Methods for generating new instances of all <see cref="PerformanceCounter"/>s
        /// we want to monitor
        /// </summary>
        private static readonly Dictionary<CounterType, Func<PerformanceCounter>> CounterGenerators =
            new Dictionary<CounterType, Func<PerformanceCounter>>()
        {
            {CounterType.Cpu, () => new
                PerformanceCounter(categoryName: "Processor", counterName: "% Processor Time",
                instanceName: "_Total", readOnly: true)},
            {CounterType.Memory, () => new
                PerformanceCounter(categoryName: "Memory", counterName: "% Committed Bytes In Use",
                readOnly: true)},
            {CounterType.Disk, () => new
                PerformanceCounter(categoryName: "LogicalDisk", counterName: "% Disk Time",
                instanceName: "_Total", readOnly: true)},
        };

        /// <summary>
        /// Methods for creating new <see cref="Series"/> with distinct colors and names
		/// corresponding to each <see cref="PerformanceCounter"/>
        /// </summary>
        private static readonly Dictionary<CounterType, Func<Series>> CounterSeries =
            new Dictionary<CounterType, Func<Series>>()
        {
            {CounterType.Cpu, () => new
                Series(CounterType.Cpu.ToString()){
                    ChartType = SeriesChartType.SplineArea, Color = Color.DarkGreen}},
            {CounterType.Memory, () => new
                Series(CounterType.Memory.ToString()){
                    ChartType = SeriesChartType.FastLine, Color = Color.MediumBlue}},
            {CounterType.Disk, () => new
                Series(CounterType.Disk.ToString()){
                    ChartType = SeriesChartType.SplineArea, Color = Color.DarkRed}},
        };

        private readonly Dictionary<CounterType, IActorRef> _counterActors;

        private readonly IActorRef _chartingActor;

        public PerformanceCounterCoordinatorActor(IActorRef chartingActor) :
            this(chartingActor, new Dictionary<CounterType, IActorRef>())
        {
        }

        public PerformanceCounterCoordinatorActor(IActorRef chartingActor,
            Dictionary<CounterType, IActorRef> counterActors)
        {
            _chartingActor = chartingActor;
            _counterActors = counterActors;

            Receive<Watch>(watch =>
            {
                if (_counterActors.ContainsKey(watch.Counter) == false)
                {
                    // create a child actor to monitor this counter if
                    // one doesn't exist already
                    var counterActor = Context.ActorOf(Props.Create(() =>
                        new PerformanceCounterActor(
                                watch.Counter.ToString(),
                                CounterGenerators[watch.Counter])));

                    // add this counter actor to our index
                    _counterActors[watch.Counter] = counterActor;
                }

                // register this series with the ChartingActor
                _chartingActor
                    .Tell(new ChartingActor.AddSeries(CounterSeries[watch.Counter]()));

                // tell the counter actor to begin publishing its
                // statistics to the _chartingActor
                _counterActors[watch.Counter]
                    .Tell(new SubscribeCounter(watch.Counter, _chartingActor));
            });

            Receive<Unwatch>(unwatch =>
            {
                if (_counterActors.ContainsKey(unwatch.Counter) == false)
                {
                    return; // noop
                }

                // unsubscribe the ChartingActor from receiving any more updates
                _counterActors[unwatch.Counter]
                    .Tell(new UnsubscribeCounter(unwatch.Counter, _chartingActor));

                // remove this series from the ChartingActor
                _chartingActor
                    .Tell(new ChartingActor.RemoveSeries(unwatch.Counter.ToString()));
            });
        }


    }
}