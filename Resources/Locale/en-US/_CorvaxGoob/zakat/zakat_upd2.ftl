# upd2: hive slave devouring
zakat-slave-devour-start-self = You sink your teeth into { $target } and start tearing at the flesh...
zakat-slave-devour-start = { CAPITALIZE(THE($user)) } bites into { $target } and tears the flesh apart!
zakat-slave-devour-interrupted = You were torn away from your prey.

# upd2: release from shackles - hive slaves only
zakat-liberate-not-slave = Your shackles are not on them - there is nothing to break.

# upd2: debug command
cmd-zakatdebugmass-desc = Adds biomass to a Zakat disease (a negative number takes it away, never below zero).
cmd-zakatdebugmass-help = Usage: zakatdebugmass <zakat uid> <amount>
cmd-zakatdebugmass-usage = Usage: zakatdebugmass <zakat uid> <amount>
cmd-zakatdebugmass-not-zakat = { $uid } is not a Zakat disease (needs the uid of an entity with ZakatComponent).
cmd-zakatdebugmass-bad-amount = "{ $amount }" is not a number.
cmd-zakatdebugmass-done = Biomass of { $zakat }: { $before } -> { $after }.
cmd-zakatdebugmass-hint-zakat = <zakat uid>
cmd-zakatdebugmass-hint-amount = <amount, may be negative>
