When your code update needs any changes to the MySQL tables, you will need to put a update script here, and also apply it in the initial sql files one folder up from here.
Files need to be named in the following format;

YYYY-MM-DD_aaemu_XXXX*.sql

Where YYYY-MM-DD is the date you introduced this change, and "XXXX" is either "login" or "game".
These scripts will get executed only one time at the start of running the Login or Game server. Installed scripts get logged into a updates table to the relevant to the server it's running for.
A server will only check and run scripts that are relevant to themselves, therefor the naming of the files is important.

For a populated database without an updates table, the updater assumes the scripts already shipped with the installation were applied.
For a database initialized by AAEmu, the snapshot baseline is recorded in AAEmu.Commons/Utils/Updater/mysql-bootstrap-baseline.txt. Add a migration to that list only when its changes are included in the bundled schema. If a migration is outside the baseline, the updater applies it through the normal update process.

The date part is technically not required, but it is used to handle update order, so please keep it consistent.
