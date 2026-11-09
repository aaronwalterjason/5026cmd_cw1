# Tablet checks

Run from `KOMA`:

```powershell
dotnet run --project TabletChecks/TabletChecks.csproj -p:BaseOutputPath=bin/tablet-check/ --no-restore
```

This builds the application (including Razor views) into a separate folder and checks closing-time boundaries, overnight hours, legacy dish statuses, partial serving, and SQL Server query translation.

Run the tablet script boundary checks with `node TabletChecks/tablet-bill.check.cjs`.

The offline checks also verify the shared demo clock, Malaysia/UTC conversion, forward/backward date changes, normal ticking, reset, disabled-mode endpoints, invalid inputs, and last-call integration.

## Time simulator

Development mode enables the floating clock automatically. For a hosted demo, explicitly set `DemoClock:Enabled` to `true` in configuration (or `DemoClock__Enabled=true` in the environment). Set it to `false` to hide the control and disable its endpoints. When enabled, all demo participants may change the shared clock.

The square clock button is draggable with mouse or touch. Its position is remembered in the browser. Click it, choose a Malaysia date/time, and select **Use demo time**. The application clock keeps ticking from that instant. Other connected screens refresh within about three seconds. **Return to real time** restores the actual date/time. Restarting the application also resets the clock.

Reservations, arrival/no-show checks, dining sessions, order/payment timestamps, last call, reports and business date defaults follow the simulated clock. Authentication tokens and cache expiry retain real elapsed time. Changing time does not change the Windows clock or undo database actions that already occurred. Save form edits before changing time. The clock is shared by browsers connected to one application process, not across multiple server instances.

For integration checks on a machine with an accessible default SQL Server LocalDB instance:

```powershell
dotnet run --project TabletChecks/TabletChecks.csproj -p:BaseOutputPath=bin/tablet-check/ --no-restore -- --database
```

The database checks create a uniquely named `KomaTabletChecks_*` database and delete only that database afterwards. They never use the application's configured database. They cover early check-in, account/table isolation, preorder submission, add-ons, combined totals, duplicate submissions, kitchen-to-dish status propagation, serving, and server-side last call.

Manual browser checks:

- Pair a tablet, scan an account with a booking ten minutes ahead, confirm the reservation, then confirm its preorder. Open Bill and verify the preorder persists. Add and confirm more food; verify both orders and their combined total remain.
- Repeat with a separate booking and choose Not me. Verify the menu has no attached preorder. Check an account without a nearby booking and a guest entry.
- Check the first-name greeting and guest greeting.
- From Kitchen, start preparation and mark an order ready. On Table service (Now), wait up to 15 seconds for `!`, select it, and verify dish names and quantities. Mark a dish served and verify its status on the customer tablet within 10 seconds. The table alert clears after all ready dishes are served.
- Use the served checkbox on the customer tablet: cancel the PIN prompt, enter an incorrect PIN, then enter the existing staff tablet PIN. Only a correct PIN can mark a ready dish served.
- With closing at 22:00, verify the warning from 21:20, and menu lock at 21:30, including an order review opened before 21:30. Bill access remains available. Times follow Malaysia time.

Existing arrival hold/no-show rules remain in force; cancelled or expired bookings are not revived by QR scanning.
