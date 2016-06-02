﻿using Betfair.ESAClient.Protocol;
using Betfair.ESASwagger.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Betfair.ESAClient.Cache
{
    /// <summary>
    /// Thread safe cache of markets
    /// </summary>
    public class MarketCache
    {
        private readonly ConcurrentDictionary<string, Market> _markets = new ConcurrentDictionary<string, Market>();
        
        /// <summary>
        /// Conflation indicates slow consumption
        /// </summary>
        public int ConflatedCount { get; internal set; }

        public void OnMarketChange(ChangeMessage<MarketChange> changeMessage)
        {
            if (changeMessage.IsStartOfNewSubscription)
            {
                //clear cache
                _markets.Clear();
            }
            if(changeMessage.Items != null)
            {
                //lazy build events
                List<MarketChangedEventArgs> batch = BatchMarketsChanged == null ? null : new List<MarketChangedEventArgs>(changeMessage.Items.Count);           

                foreach (MarketChange marketChange in changeMessage.Items)
                {
                    Market market = OnMarketChange(marketChange);

                    if(IsMarketRemovedOnClose && market.IsClosed)
                    {
                        //remove on close
                        Market removed;
                        _markets.TryRemove(market.MarketId, out removed);
                    }

                    //lazy build events
                    if(batch != null || MarketChanged != null)
                    {
                        MarketChangedEventArgs arg = new MarketChangedEventArgs() { Change = marketChange, Market = market };
                        if(MarketChanged != null)
                        {
                            DispatchMarketChanged(arg);
                        }
                        if(batch != null)
                        {
                            batch.Add(arg);
                        }
                    }
                }
                if(batch != null)
                {
                    DispatchBatchMarketsChanged(new BatchMarketsChangedEventArgs() { Changes = batch });
                }
            }
        }



        private Market OnMarketChange(MarketChange marketChange)
        {
            if(marketChange.Con == true)
            {
                ConflatedCount++;
            }
            Market market = _markets.GetOrAdd(marketChange.Id, id => new Market(this, id));
            market.OnMarketChange(marketChange);
            return market;
        }

        private void DispatchMarketChanged(MarketChangedEventArgs args)
        {
            try
            {
                MarketChanged.Invoke(this, args);
            }
            catch(Exception e)
            {
                Trace.TraceError("Error dispatching event: {0}", e);
            }            
        }

        private void DispatchBatchMarketsChanged(BatchMarketsChangedEventArgs args)
        {
            try
            {
                BatchMarketsChanged.Invoke(this, args);
            }
            catch (Exception e)
            {
                Trace.TraceError("Error dispatching event: {0}", e);
            }
        }

        /// <summary>
        /// Wether markets are automatically removed on close
        /// (default is true)
        /// </summary>
        public bool IsMarketRemovedOnClose { get; set; } = true;

        /// <summary>
        /// Event for each market change
        /// </summary>
        public event MarketChangedEventHandler MarketChanged;

        /// <summary>
        /// Event for each batch of market changes
        /// (note to be truly atomic you will want to set to merge segments
        /// otherwise an event could be segmented)
        /// </summary>
        public event BatchMarketsChangedEventHandler BatchMarketsChanged;

        /// <summary>
        /// Queries by market id - the result is invariant for the 
        /// lifetime of the market.
        /// </summary>
        /// <param name="marketid"></param>
        /// <returns></returns>
        public Market this[string marketid]
        {
            get
            {
                return _markets[marketid];
            }
        }

        /// <summary>
        /// All the cached markets
        /// </summary>
        public IEnumerable<Market> Markets
        {
            get
            {
                return _markets.Values;
            }
        }

        /// <summary>
        /// Market count
        /// </summary>
        public int Count
        {
            get
            {
                return _markets.Count;
            }
        }
    }

    public delegate void MarketChangedEventHandler(object sender, MarketChangedEventArgs e);

    public delegate void BatchMarketsChangedEventHandler(object sender, BatchMarketsChangedEventArgs e);

    public class MarketChangedEventArgs : EventArgs
    {
        public MarketChange Change { get; internal set; }

        public Market Market { get; internal set; }

        public MarketSnap Snap
        {
            get
            {
                return Market.Snap;
            }
        }
    }

    public class BatchMarketsChangedEventArgs : EventArgs
    {
        public IList<MarketChangedEventArgs> Changes { get; internal set; }
    }
}