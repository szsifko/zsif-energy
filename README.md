# zsif-energy

.NET 10 worker that reads grid import/export data from the Netz Burgenland smart meter portal and publishes it to Home Assistant via MQTT (with auto discovery).

## How it works

- Logs in to the Netz Burgenland smart meter portal (enVIEW) and polls the 15-minute values of two metering points: import (1.8.0) and export (2.8.0).
- Calculates today's and yesterday's energy, the latest interval power and the meter reading.
- Publishes the values as retained MQTT messages and registers the sensors in Home Assistant via MQTT discovery.

> The portal delivers values with a delay of several hours, so the data is not suitable for live control.

## Home Assistant sensors

All sensors belong to the device **Netz Burgenland Smart Meter**.

| Sensor | Unit |
|---|---|
| `grid_import_energy_today` / `grid_export_energy_today` | kWh |
| `grid_import_energy_yesterday` / `grid_export_energy_yesterday` | kWh |
| `grid_import_power` / `grid_export_power` | W |
| `grid_import_meter_reading` / `grid_export_meter_reading` | kWh |
| `grid_meter_last_update` | timestamp |

State topics live under `zsif/gridmeter/...`, availability under `zsif/gridmeter/status`.

## Configuration

Settings are in `src/Zsif.Energy.Worker/appsettings.json`. Credentials and metering points must not be committed; set them via user secrets (local) or environment variables (server).

| Key | Description |
|---|---|
| `Smartmeter:Username` / `Smartmeter:Password` | Portal login |
| `Smartmeter:ImportMeteringPoint` / `Smartmeter:ExportMeteringPoint` | Metering point IDs |
| `Smartmeter:PollInterval` | Poll interval (default `00:15:00`) |
| `Mqtt:Host` / `Mqtt:Port` | MQTT broker |
| `Mqtt:Username` / `Mqtt:Password` | MQTT login (optional) |
| `Mqtt:TopicPrefix` / `Mqtt:NodeId` | Topic prefix and discovery node id |

Local setup with user secrets:

```bash
cd src/Zsif.Energy.Worker
dotnet user-secrets set "Smartmeter:Username" "<user>"
dotnet user-secrets set "Smartmeter:Password" "<password>"
dotnet user-secrets set "Smartmeter:ImportMeteringPoint" "<id>"
dotnet user-secrets set "Smartmeter:ExportMeteringPoint" "<id>"
dotnet user-secrets set "Mqtt:Host" "<broker>"
```

As environment variables use double underscores, e.g. `Smartmeter__Username`.

## Build, test, run

```bash
dotnet build
dotnet test
dotnet run --project src/Zsif.Energy.Worker
```

## Deployment (Debian LXC on Proxmox)

The release is a self-contained single-file binary, no .NET runtime is needed in the container.

### 1. Create the LXC (once)

In Proxmox: Create CT with the Debian 12 template, unprivileged, 1 core, 256 MB RAM (the worker uses about 75 MB), 4 GB disk, network in the same LAN as Home Assistant. Enable *Start at boot*.

### 2. Build the release (Windows)

```powershell
./scripts/publish-linux.ps1
```

Creates `artifacts/zsif-energy-linux-x64.tar.gz` (binary, systemd unit, env template, install script).

### 3. Copy it into the container

Via the Proxmox host (`<ctid>` = container ID):

```bash
scp artifacts/zsif-energy-linux-x64.tar.gz root@<proxmox-host>:/tmp/
pct push <ctid> /tmp/zsif-energy-linux-x64.tar.gz /root/zsif-energy-linux-x64.tar.gz
pct enter <ctid>
```

### 4. Install (inside the container, as root)

```bash
cd /root
tar -xzf zsif-energy-linux-x64.tar.gz
bash zsif-energy-linux-x64/install.sh
```

On the first install the script creates `/etc/zsif-energy/zsif-energy.env` (root only) and stops. Fill in the credentials there, then:

```bash
systemctl start zsif-energy
journalctl -u zsif-energy -f
```

The service runs as user `zsif-energy`, starts on boot and restarts on failure.

### Update

Build and copy a new release as above, then run `bash zsif-energy-linux-x64/install.sh` again. The env file is kept.

## Roadmap

- [x] Portal API and energy calculation
- [x] MQTT publishing with Home Assistant discovery
- [ ] House consumption via Fronius Solar API
- [x] Deployment files for a Debian LXC as systemd service
- [ ] First deployment and check in Home Assistant
