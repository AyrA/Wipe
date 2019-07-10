# WIPE

Disk wiping utility

# CAUTION

As the name suggests,
this utility will destroy all data on the selected disk.

## Functionality

This utility wipes the contents of entire disks and partitions.

## Limitations

This utility comes with a few limitations.

### Permissions

You need administrative permissions to use this utility.

### In-Use media

You can't overwrite media that is currently in use by Windows.
If you encounter a stubborn disk,
run `diskmgmt.msc` or `diskpart.exe` and delete all partitions on said disk before wiping it.



## How to use

This utility has a menu. Just double click and select the option of your choice.

## Disk vs Partition

In most cases you want to overwrite the entire disk.

Overwriting individual partitions should only be done
when the disk contains multiple partitions and you don't want to touch the others.
When you overwrite a partition you will not remove the partition itself,
only the contents of it.
To remove the partition after wiping it, use the disk management utility of your operating system.

## Safety

By default you can't overwrite fixed disks, only removable media.
You can disable this protection in the settings.

## Selecting the correct disk

If you are unsure which disk is the correct one,
especially because they are of the same model/size, you can do this:

1. Unplug your device
2. Select the appropriate disk or partition wipe option to bring up the list of disks/partitions
3. Take note of the list contents
4. Plug your device back in and wait for windows to recognize it
5. Use the "refresh" option in the list
6. Select the option that just appeared

If you just want to erase the content of a single partition and not the entire disk,
you can alternatively give said partition a distinctive volume label because they are displayed in the menu.

## Resuming progress

You can cancel a disk/partition wipe operation at any time using the `[ESC]` key.
If you try to wipe the same disk/partition again,
the utility will continue where it was cancelled.

You can delete the progress memory in the settings.

## Fault tollerance

If part of the media is damaged,
it tries to weasel itself around the damaged part.
This is achieved by trying to write single bytes,
and if not successful, seeking over that part.
The bytes in the damaged part are possibly left unchanged by this method.

Instead of wiping a damaged disk, consider destroying it mechanically.

If damaged bytes are encountered,
it will show a report of the number of damaged bytes.
You are strongly advised to no longer use damaged media.
If you absolutely have to, perform a "long format" or a surface check before using it.
The system will then mark the damaged parts and no longer use it.

## Multiple passes

This application will overwrite your disk only once.
If you want to overwrite it multiple times,
you can just rerun the utility.
Various data recovery laboratories agree that a single pass is suficient.

Instead of writing all zeros we offer a "random" method selection.

## Multiple disks

The application will only overwrite a single disk/partition at a time and then has to be restarted.
To overwrite multiple disks simultaneously, launch multiple instances.

**Note:** Try to not overwrite multiple partitions on the same disk at the same time.
You will have massively degraded performance.

## SSD

Solid state drives require special handling because of something called [wear leveling](https://en.wikipedia.org/wiki/Wear_leveling).
Wear leveling ensures that the data blocks are used evenly because on SSD drives, the number of write cycles is limited.
This means when you overwrite a certain block, there is no guarantee that it isn't remaped somewhere else and the physical data still persists.

To overwrite solid state drives properly, do as follows:

1. Create a single partition that consumes the entire drive and format with any file system of your choice.
2. Use a tool like [SDelete](https://docs.microsoft.com/en-us/sysinternals/downloads/sdelete) to zero the free space.
3. Launch this wipe utility and overwrite the first few megabytes of the disk with zeros.

## Patterns

The utility supports multiple patterns you can write to the disk.
From a data wiping standpoint, they are all just as effective.

### Zeros

This is the default. It writes all zeros to the disk.

### Alternating

This writes the byte `0xAA` to the disk.
This byte is an alternating pattern of ones and zeros.

### Ones

The opposite of the zero method. Writes all ones to the disk.

### Random

This writes random data to the disk.

### RandomCrypto

This is the exact same as the random data method,
but uses a cryptographically safe random number generator.

The only difference between this and the regular random method is
that the random data in the regular method will eventually become predictable.

They're both just as good at overwriting your data.

**Note:** This method can be significantly slower than regular random data.
