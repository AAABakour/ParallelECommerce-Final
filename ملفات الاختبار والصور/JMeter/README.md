# JMeter Test Files

This folder contains the Apache JMeter `.jmx` files required for delivery.

## Official file for the first 5 requirements

- `ParallelECommerce_First5.jmx`

This test plan covers:

1. Race Condition before/after
2. Resource Management / Capacity Control before/after
3. Asynchronous Queue before/after
4. Batch Processing before/after
5. Load Balancing before/after and health-check behavior

Run it from JMeter GUI and capture screenshots from:

- Summary Report
- View Results Tree
- Aggregate Report, if needed

The generated `.jtl` file is saved to:

`Tests/JMeter/results/first5_results.jtl`
