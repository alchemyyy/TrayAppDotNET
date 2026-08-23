#pragma once

#include <Windows.h>
#include <cstddef>
#include <cstdint>

#define KILL_HELPER_MAILBOX_MAGIC 0x4B484D54U
#define KILL_HELPER_PROTOCOL_VERSION 1U
#define KILL_HELPER_MAILBOX_SIZE 4096U

#define KILL_HELPER_STATE_STARTING 1L
#define KILL_HELPER_STATE_READY 2L
#define KILL_HELPER_STATE_STOPPING 3L
#define KILL_HELPER_STATE_FAILED 4L

#define KILL_HELPER_CONTROL_SHUTDOWN 0x00000001L

#define KILL_HELPER_FLAG_MAILBOX_LOCKED 0x00000001L
#define KILL_HELPER_FLAG_STATE_LOCKED 0x00000002L
#define KILL_HELPER_FLAG_HOT_CODE_LOCKED 0x00000004L
#define KILL_HELPER_FLAG_KERNEL_CODE_LOCKED 0x00000008L
#define KILL_HELPER_FLAG_HIGH_PRIORITY 0x00000010L
#define KILL_HELPER_FLAG_DEBUG_PRIVILEGE 0x00000020L
#define KILL_HELPER_FLAG_POWER_THROTTLING_DISABLED 0x00000040L
#define KILL_HELPER_FLAG_STACK_LOCKED 0x00000080L
#define KILL_HELPER_FLAG_LOCK_CAPACITY_RESERVED 0x00000100L

#define KILL_HELPER_RESULT_NONE 0L
#define KILL_HELPER_RESULT_SUCCESS 1L
#define KILL_HELPER_RESULT_INVALID_TARGET 2L
#define KILL_HELPER_RESULT_OPEN_FAILED 3L
#define KILL_HELPER_RESULT_IDENTITY_MISMATCH 4L
#define KILL_HELPER_RESULT_CRITICAL_PROCESS 5L
#define KILL_HELPER_RESULT_TERMINATE_FAILED 6L

#pragma pack(push, 8)
struct alignas(64) KillHelperMailbox
{
    std::uint32_t Magic;
    std::uint32_t Version;
    volatile LONG HelperState;
    std::uint32_t HelperProcessID;
    volatile LONG HelperFlags;
    volatile LONG HelperStartupError;
    std::uint32_t ParentProcessID;
    volatile LONG ControlFlags;
    std::uint8_t HeaderPadding[32];

    volatile LONG64 ArmPayloadSequence;
    volatile LONG64 ArmRequestSequence;
    LONG64 ArmGeneration;
    LONG64 ArmCreationTime;
    LONG ArmProcessID;
    LONG ArmReserved;
    std::uint8_t ArmPadding[24];

    volatile LONG64 FirePayloadSequence;
    volatile LONG64 FireRequestSequence;
    LONG64 FireGeneration;
    LONG64 FireCreationTime;
    LONG FireProcessID;
    std::uint32_t FireExitCode;
    std::uint8_t FirePadding[24];

    volatile LONG64 FireResponseSequence;
    volatile LONG FireResult;
    volatile LONG FireError;
    volatile LONG FireResponseProcessID;
    LONG FireResponseReserved;
    std::uint8_t ResponsePadding[KILL_HELPER_MAILBOX_SIZE - 216U];
};
#pragma pack(pop)

static_assert(sizeof(KillHelperMailbox) == KILL_HELPER_MAILBOX_SIZE);
static_assert(offsetof(KillHelperMailbox, ArmPayloadSequence) == 64U);
static_assert(offsetof(KillHelperMailbox, FirePayloadSequence) == 128U);
static_assert(offsetof(KillHelperMailbox, FireResponseSequence) == 192U);

struct KillHelperTarget
{
    LONG ProcessID;
    LONG64 CreationTime;
    LONG64 Generation;
};
