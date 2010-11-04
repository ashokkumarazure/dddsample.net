using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using DomainDrivenDelivery.Application.Utilities;
using DomainDrivenDelivery.Domain.Model.Freight;
using DomainDrivenDelivery.Domain.Model.Handling;
using DomainDrivenDelivery.Domain.Model.Locations;
using DomainDrivenDelivery.Domain.Model.Shared;
using DomainDrivenDelivery.Domain.Model.Travel;

using NUnit.Framework;

using L = DomainDrivenDelivery.Domain.Model.Locations.SampleLocations;
using V = DomainDrivenDelivery.Domain.Model.Travel.SampleVoyages;

namespace Infrastructure.Tests.Persistence.NHibernate
{
    [TestFixture]
    public class CargoRepositoryTest : AbstractRepositoryTest
    {
        private CargoRepository cargoRepository;
        private LocationRepository locationRepository;
        private VoyageRepository voyageRepository;

        public CargoRepository CargoRepository
        {
            set { this.cargoRepository = value; }
        }

        public LocationRepository LocationRepository
        {
            set { this.locationRepository = value; }
        }

        public VoyageRepository VoyageRepository
        {
            set { this.voyageRepository = value; }
        }

        [Test]
        public void testFindByCargoId()
        {
            TrackingId trackingId = new TrackingId("FGH");
            Cargo cargo = cargoRepository.find(trackingId);

            Assert.AreEqual(L.HONGKONG, cargo.RouteSpecification.origin());
            Assert.AreEqual(L.HELSINKI, cargo.RouteSpecification.destination());

            IEnumerable<HandlingEvent> events =
                HandlingEventRepository.lookupHandlingHistoryOfCargo(cargo).distinctEventsByCompletionTime();
            Assert.AreEqual(2, events.Count());

            HandlingEvent firstEvent = events.ElementAt(0);
            assertHandlingEvent(cargo, firstEvent, HandlingActivityType.RECEIVE, L.HONGKONG, 100, 160, Voyage.NONE);

            HandlingEvent secondEvent = events.ElementAt(1);

            Voyage hongkongMelbourneTokyoAndBack =
                new Voyage.Builder(new VoyageNumber("0303"), L.HONGKONG).addMovement(L.MELBOURNE,
                    new DateTime(1),
                    new DateTime(2)).addMovement(L.TOKYO, new DateTime(3), new DateTime(4)).addMovement(L.HONGKONG,
                        new DateTime(5),
                        new DateTime(6)).build();

            assertHandlingEvent(cargo,
                secondEvent,
                HandlingActivityType.LOAD,
                L.HONGKONG,
                150,
                110,
                hongkongMelbourneTokyoAndBack);

            IEnumerable<Leg> legs = cargo.Itinerary.legs();
            Assert.AreEqual(3, legs.Count());

            Leg firstLeg = legs.ElementAt(0);
            assertLeg(firstLeg, "0101", L.HONGKONG, L.MELBOURNE);

            Leg secondLeg = legs.ElementAt(1);
            assertLeg(secondLeg, "0101", L.MELBOURNE, L.STOCKHOLM);

            Leg thirdLeg = legs.ElementAt(2);
            assertLeg(thirdLeg, "0101", L.STOCKHOLM, L.HELSINKI);
        }

        private void assertHandlingEvent(Cargo cargo,
            HandlingEvent @event,
            HandlingActivityType expectedEventType,
            Location expectedLocation,
            int completionTimeMs,
            int registrationTimeMs,
            Voyage voyage)
        {
            Assert.AreEqual(expectedEventType, @event.Type);
            Assert.AreEqual(expectedLocation, @event.Location);

            DateTime expectedCompletionTime = SampleDataGenerator.offset(completionTimeMs);
            Assert.AreEqual(expectedCompletionTime, @event.CompletionTime);

            DateTime expectedRegistrationTime = SampleDataGenerator.offset(registrationTimeMs);
            Assert.AreEqual(expectedRegistrationTime, @event.RegistrationTime);

            Assert.AreEqual(voyage, @event.Voyage);
            Assert.AreEqual(cargo, @event.Cargo);
        }

        [Test]
        public void testFindByCargoIdUnknownId()
        {
            Assert.IsNull(cargoRepository.find(new TrackingId("UNKNOWN")));
        }

        private void assertLeg(Leg firstLeg, String vn, Location expectedFrom, Location expectedTo)
        {
            Assert.AreEqual(new VoyageNumber(vn), firstLeg.Voyage.VoyageNumber);
            Assert.AreEqual(expectedFrom, firstLeg.LoadLocation);
            Assert.AreEqual(expectedTo, firstLeg.UnloadLocation);
        }

        [Test]
        public void testSave()
        {
            TrackingId trackingId = new TrackingId("AAA");
            Location origin = locationRepository.find(L.STOCKHOLM.UnLocode);
            Location destination = locationRepository.find(L.MELBOURNE.UnLocode);

            Cargo cargo = new Cargo(trackingId, new RouteSpecification(origin, destination, DateTime.Now));
            cargoRepository.store(cargo);

            getSession().Flush();
            getSession().Clear();

            cargo = cargoRepository.find(trackingId);
            Assert.IsNull(cargo.Itinerary);

            cargo.assignToRoute(
                new Itinerary(Leg.deriveLeg(voyageRepository.find(new VoyageNumber("0101")),
                    locationRepository.find(L.STOCKHOLM.UnLocode),
                    locationRepository.find(L.MELBOURNE.UnLocode))));

            flush();

            var map = GenericTemplate.QueryForObjectDelegate(CommandType.Text,
                String.Format("select * from Cargo where tracking_id = '{0}'", trackingId.stringValue()),
                (r, i) => new {TRACKING_ID = r["TRACKING_ID"]});

            Assert.AreEqual("AAA", map.TRACKING_ID);

            long originId = (long) getSession().GetIdentifier(cargo);
            //Assert.AreEqual(originId, map.get("SPEC_ORIGIN_ID"));

            long destinationId = (long) getSession().GetIdentifier(cargo);
            //Assert.AreEqual(destinationId, map.get("SPEC_DESTINATION_ID"));

            getSession().Clear();

            Cargo loadedCargo = cargoRepository.find(trackingId);
            Assert.AreEqual(1, loadedCargo.Itinerary.legs().Count());
        }

        [Test]
        public void testReplaceItinerary()
        {
            Cargo cargo = cargoRepository.find(new TrackingId("FGH"));
            long cargoId = (long) getSession().GetIdentifier(cargo);
            Assert.AreEqual(3,
                GenericTemplate.ExecuteScalar(CommandType.Text,
                    String.Format("select count(*) from Leg where cargo_id = {0}", cargoId)));

            Location legFrom = locationRepository.find(new UnLocode("DEHAM"));
            Location legTo = locationRepository.find(new UnLocode("FIHEL"));
            Itinerary newItinerary = new Itinerary(Leg.deriveLeg(V.atlantic2, legFrom, legTo));

            cargo.assignToRoute(newItinerary);

            cargoRepository.store(cargo);
            flush();

            Assert.AreEqual(1,
                GenericTemplate.ExecuteScalar(CommandType.Text,
                    String.Format("select count(*) from Leg where cargo_id = {0}", cargoId)));
        }

        [Test]
        public void testFindAll()
        {
            IEnumerable<Cargo> all = cargoRepository.findAll();
            CollectionAssert.AllItemsAreNotNull(all);
            Assert.AreEqual(6, all.Count());
        }

        [Test]
        public void testFindCargosOnVoyage()
        {
            Voyage voyage = voyageRepository.find(new VoyageNumber("0101"));
            IEnumerable<Cargo> cargos = cargoRepository.findCargosOnVoyage(voyage);
            Assert.AreEqual(3, cargos.Count());
            foreach(Cargo cargo in cargos)
            {
                bool found = false;
                foreach(Leg leg in cargo.Itinerary.legs())
                {
                    if(leg.Voyage.sameAs(voyage))
                    {
                        found = true;
                    }
                }
                Assert.True(found, "Cargo " + cargo + " has no leg on voyage " + voyage);
            }

            Voyage voyage2 = voyageRepository.find(new VoyageNumber("0100S"));
            Assert.True(!cargoRepository.findCargosOnVoyage(voyage2).Any());
        }
    }
}