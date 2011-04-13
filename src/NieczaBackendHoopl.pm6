use NieczaBackendNAM;
class NieczaBackendHoopl is NieczaBackendNAM;

# XXX XXX .NET doesn't seem to have any real spawnl functionality,
# only system
sub run_command($cmd, $args) {
    Q:CgOp { (rnull (rawscall Builtins,Kernel.RunSubtask (obj_getstr {$cmd}) (obj_getstr {$args}))) };
}

method post_save($name, :$main) {
    # None needed; run does all the work
}

method run($name) {
    my $fname = $name.split('::').join('.');
    run_command("hoopl/dist/build/niecza-hoopl/niecza-hoopl", "obj/" ~ $fname ~ ".nam");
}
