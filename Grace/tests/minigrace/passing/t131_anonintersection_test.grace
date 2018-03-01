type A = {
    bar -> String
}

type B = A & interface {
    quux -> Number
}

method btaker(b : B) {
    print "{b.bar}{b.quux}"
}

btaker(object {
    def bar is public = "YE"
    def quux is public = 5
})
