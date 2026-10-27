"""Fix upstream out-of-source SCM lookup; native states must carry the real pin.

No game/state migration. Only the newly built Linux core's metadata is changed;
old installed Citra binaries and their save partitions remain intact.
"""
from pathlib import Path
import subprocess
import sys

root=Path(sys.argv[1])
path=root/'CMakeModules/GenerateBuildInfo.cmake'
original=subprocess.check_output(['git','-C',str(root),'show','HEAD:CMakeModules/GenerateBuildInfo.cmake']).decode()
before='''    get_git_head_revision(GIT_REF_SPEC GIT_REV)
    git_describe(GIT_DESC --always --long --dirty)
    git_branch_name(GIT_BRANCH)'''
after='''    # Home: script-mode CMAKE_CURRENT_SOURCE_DIR is the build dir, not the repo.
    execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${SRC_DIR}" rev-parse HEAD OUTPUT_VARIABLE GIT_REV OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
    execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${SRC_DIR}" describe --always --long OUTPUT_VARIABLE GIT_DESC OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
    set(GIT_BRANCH "HEAD")
    string(LENGTH "${GIT_REV}" HOME_REV_LENGTH)
    if(NOT HOME_REV_LENGTH EQUAL 40)
        message(FATAL_ERROR "Home: valid 40-character Citra revision required for native states")
    endif()'''
if original.count(before)!=1:raise RuntimeError('Pinned Citra SCM context changed.')
patched=original.replace(before,after)
previous=patched
patched=patched.replace('get_timestamp(BUILD_DATE)','execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${SRC_DIR}" show -s --format=%cI HEAD OUTPUT_VARIABLE BUILD_DATE OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)')
if path.read_text() not in (original,previous,patched):raise RuntimeError('Unexpected cached Citra SCM modifications.')
path.write_text(patched)
