CREATE INDEX IF NOT EXISTS ix_trips_company_state ON trips(company_id,status,execution_state);
CREATE INDEX IF NOT EXISTS ix_positions_vehicle_time ON vehicle_position_events(company_id,vehicle_id,timestamp DESC);
CREATE INDEX IF NOT EXISTS ix_orders_company_status ON transport_orders(company_id,status,created_at DESC);
CREATE INDEX IF NOT EXISTS ix_routes_trip_status ON trip_routes(company_id,trip_id,status,created_at DESC);
CREATE INDEX IF NOT EXISTS ix_dispatch_driver_status ON route_dispatches(company_id,driver_id,status,created_at DESC);
CREATE INDEX IF NOT EXISTS ix_alerts_company_status ON operational_alerts(company_id,status,created_at DESC);
CREATE INDEX IF NOT EXISTS ix_waiting_company_trip ON waiting_events(company_id,trip_id,started_at DESC);
CREATE INDEX IF NOT EXISTS ix_border_company_trip ON border_crossings(company_id,trip_id,entered_at DESC);
CREATE INDEX IF NOT EXISTS ix_cost_company_trip ON transport_costs(company_id,trip_id,created_at DESC);
