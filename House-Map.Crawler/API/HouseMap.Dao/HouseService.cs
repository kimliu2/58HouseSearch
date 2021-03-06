using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Text;
using HouseMap.Common;
using HouseMap.Dao;
using HouseMap.Dao.DBEntity;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Linq;
using System.Globalization;

namespace HouseMap.Dao
{

    public class HouseService
    {

        private readonly RedisTool _redisTool;



        private readonly HouseDapper _newHouseDapper;

        private readonly ElasticService _elasticService;


        private readonly ConfigService _configService;

        public HouseService(RedisTool RedisTool, ConfigService configService,
         HouseDapper newHouseDapper, ElasticService elasticService)
        {
            _redisTool = RedisTool;
            _configService = configService;
            _newHouseDapper = newHouseDapper;
            _elasticService = elasticService;
        }

        private List<DBHouse> NewDBSearch(HouseCondition condition)
        {
            if (condition == null || condition.City == null)
            {
                throw new Exception("查询条件不能为null");
            }
            var houses = _redisTool.ReadCache<List<DBHouse>>(condition.RedisKey, RedisKey.NewHouses.DBName);
            if (houses == null || condition.Refresh)
            {
                houses = !string.IsNullOrEmpty(condition.Keyword) ? _elasticService.Query(condition) : _newHouseDapper.SearchHouses(condition);
                if (houses != null && !houses.Any())
                {
                    _redisTool.WriteObject(condition.RedisKey, houses, RedisKey.NewHouses.DBName);
                }
            }
            return houses;
        }


        public IEnumerable<DBHouse> NewSearch(HouseCondition condition)
        {
            if (condition == null)
            {
                return default(List<DBHouse>);
            }
            if (string.IsNullOrEmpty(condition.Source) && string.IsNullOrEmpty(condition.Keyword))
            {
                var houseList = new List<DBHouse>();
                // 获取当前城市的房源配置
                var cityConfigs = _configService.LoadSources(condition.City);
                if (cityConfigs.Count == 0)
                {
                    return houseList;
                }
                var limitCount = condition.Size / cityConfigs.Count;
                foreach (var config in cityConfigs)
                {
                    condition.Source = config.Source;
                    condition.Size = limitCount;
                    houseList.AddRange(NewDBSearch(condition));
                }
                return houseList.OrderByDescending(h => h.PubTime);
            }
            else
            {
                return NewDBSearch(condition);
            }
        }


        public DBHouse FindById(string houseId)
        {
            var redisKey = RedisKey.HouseDetail;
            var house = _redisTool.ReadCache<DBHouse>(redisKey.Key + houseId, redisKey.DBName);
            if (house == null)
            {
                house = _newHouseDapper.FindById(houseId);
                if (house == null)
                {
                    return null;
                }
                _redisTool.WriteObject(redisKey.Key + houseId, house, redisKey.DBName, (int)redisKey.ExpireTime.TotalMinutes);

            }
            return house;
        }


        public void UpdateLngLat(string houseId, string lng,string lat)
        {
            if (string.IsNullOrEmpty(lat) || string.IsNullOrEmpty(lng))
            {
                throw new Exception("lat and lng not empty.");
            }
            var house = FindById(houseId);
            if (house == null)
            {
                throw new Exception($"{houseId} not found.");
            }
            house.Latitude = lat;
            house.Longitude = lng;
            _newHouseDapper.UpdateLngLat(house);
            var redisKey = RedisKey.HouseDetail;
            _redisTool.WriteObject(redisKey.Key + houseId, house, redisKey.DBName, (int)redisKey.ExpireTime.TotalMinutes);
        }

        public void RefreshHouseV2()
        {
            LogHelper.Info("开始RefreshHouseV2...");
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var cityDashboards = _configService.LoadCitySources();
            foreach (var item in cityDashboards)
            {
                var search = new HouseCondition() { City = item.Key, Size = 600, IntervalDay = 14, Refresh = true };
                foreach (var dashboard in item.Value)
                {
                    //指定来源,每次拉600条,一般用于地图页
                    for (var page = 0; page <= 3; page++)
                    {
                        search.Size = 600;
                        search.Page = page;
                        search.Source = dashboard.Source;
                        NewSearch(search);
                    }

                    // 指定来源,每次拉20条,前30页,一般用于小程序/移动端列表页
                    for (var page = 0; page <= 30; page++)
                    {
                        search.Size = 20;
                        search.Source = dashboard.Source;
                        search.Page = page;
                        this.NewSearch(search);
                    }
                }
            }
            sw.Stop();
            string copyTime = sw.Elapsed.TotalSeconds.ToString(CultureInfo.InvariantCulture);
            LogHelper.Info("RefreshHouseV2结束，花费时间：" + copyTime);
        }


        public void RefreshHouseV3()
        {
            LogHelper.Info("RefreshHouseV3...");
            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            var cityDashboards = _configService.LoadCitySources();
            foreach (var item in cityDashboards)
            {
                //无指定来源,前600条数据
                var search = new HouseCondition() { City = item.Key, Size = 600, IntervalDay = 14, Refresh = true };
                for (var page = 0; page <= 5; page++)
                {
                    search.Page = page;
                    NewSearch(search);
                }

                //无指定来源,每次拉180条,一共10页,一般用于移动端地图
                for (var page = 0; page <= 10; page++)
                {
                    search.Source = "";
                    search.Size = 180;
                    search.Page = page;
                    this.NewSearch(search);
                }

                //无指定来源,每次拉20条,一共30页,一般用于小程序或者移动端列表
                for (var page = 0; page <= 30; page++)
                {
                    search.Source = "";
                    search.Size = 20;
                    search.Page = page;
                    this.NewSearch(search);
                }
            }
            sw.Stop();
            string copyTime = sw.Elapsed.TotalSeconds.ToString(CultureInfo.InvariantCulture);
            LogHelper.Info("RefreshHouseV2结束，花费时间：" + copyTime);
        }

    }

}