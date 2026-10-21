namespace Marey

    [<Measure>]
    type m = Data.UnitSystems.SI.UnitNames.metre

    [<Measure>]
    type kg = Data.UnitSystems.SI.UnitNames.kilogram

    [<Measure>]
    type s = Data.UnitSystems.SI.UnitNames.second

    [<Measure>]
    type A = Data.UnitSystems.SI.UnitNames.ampere

    [<Measure>]
    type K = Data.UnitSystems.SI.UnitNames.kelvin

    [<Measure>]
    type mol = Data.UnitSystems.SI.UnitNames.mole

    [<Measure>]
    type cd = Data.UnitSystems.SI.UnitNames.candela

    [<AutoOpen>]
    module SiWitnesses =

        [<RequireQualifiedAccess>]
        module U =

            val metre: U<m>

            val kilogram: U<kg>

            val second: U<s>

            val ampere: U<A>

            val kelvin: U<K>

            val mole: U<mol>

            val candela: U<cd>

            val squareMetre: U<m ^ 2>

            val cubicMetre: U<m ^ 3>

            val metrePerSecond: U<m/s>

            val metrePerSecondSquared: U<m/s ^ 2>

            val kilogramPerCubicMetre: U<kg/m ^ 3>

            val perSecond: U</s>

            val gram: U<kg>

namespace Marey

    [<Measure>]
    type Hz =/s

    [<Measure>]
    type N = kg m/s ^ 2

    [<Measure>]
    type Pa = N/m ^ 2

    [<Measure>]
    type J = N m

    [<Measure>]
    type W = J/s

    [<Measure>]
    type C = A s

    [<Measure>]
    type V = W/A

    [<Measure>]
    type F = C/V

    [<Measure>]
    type ohm = V/A

    [<Measure>]
    type S = A/V

    [<Measure>]
    type Wb = V s

    [<Measure>]
    type T = Wb/m ^ 2

    [<Measure>]
    type H = Wb/A

    [<Measure>]
    type sr = rad ^ 2

    [<Measure>]
    type lm = cd sr

    [<Measure>]
    type lx = lm/m ^ 2

    [<Measure>]
    type Bq =/s

    [<Measure>]
    type Gy = J/kg

    [<Measure>]
    type Sv = J/kg

    [<Measure>]
    type kat = mol/s

    [<AutoOpen>]
    module DerivedWitnesses =

        [<RequireQualifiedAccess>]
        module U =

            val hertz: U<Hz>

            val newton: U<N>

            val pascal: U<Pa>

            val joule: U<J>

            val watt: U<W>

            val coulomb: U<C>

            val volt: U<V>

            val farad: U<F>

            val ohm: U<ohm>

            val siemens: U<S>

            val weber: U<Wb>

            val tesla: U<T>

            val henry: U<H>

            val lumen: U<lm>

            val lux: U<lx>

            val becquerel: U<Bq>

            val gray: U<Gy>

            val sievert: U<Sv>

            val katal: U<kat>

            val radian: U<rad>

            val steradian: U<sr>

namespace Marey

    [<Measure>]
    type eV

    [<Measure>]
    type angstrom

    [<Measure>]
    type bar

    [<Measure>]
    type atm

    [<Measure>]
    type L

    [<Measure>]
    type minute

    [<Measure>]
    type h

    [<Measure>]
    type d

    [<Measure>]
    type yr

    [<Measure>]
    type degC

    [<Measure>]
    type degF

    [<Measure>]
    type au

    [<Measure>]
    type pc

    [<Measure>]
    type ly

    [<Measure>]
    type Da

    [<Measure>]
    type barn

    [<Measure>]
    type Gs

    [<Measure>]
    type cal

    [<AutoOpen>]
    module NonSiWitnesses =

        [<RequireQualifiedAccess>]
        module U =

            val electronvolt: U<eV>

            val calorie: U<cal>

            val angstrom: U<angstrom>

            val astronomicalUnit: U<au>

            val lightYear: U<ly>

            val parsec: U<pc>

            val barn: U<barn>

            val litre: U<L>

            val bar: U<bar>

            val atmosphere: U<atm>

            val minute: U<minute>

            val hour: U<h>

            val day: U<d>

            val year: U<yr>

            val dalton: U<Da>

            val gauss: U<Gs>

            val degreeCelsius: U<degC>

            val degreeFahrenheit: U<degF>

namespace Marey

    type UnitSymbol =
        {

          Unicode: string

          Ascii: string

          LatexCore: string

          Siunitx: string
        }

    [<RequireQualifiedAccess>]
    module UnitSymbol =

        val plain: text: string -> siunitx: string -> UnitSymbol

        val create:
          unicode: string ->
            ascii: string -> latexCore: string -> siunitx: string -> UnitSymbol

    [<Sealed>]
    type UnitRegistry =

        new: entries: (Dim * UnitSymbol) array -> UnitRegistry

        member TryCanonical: dim: Dim -> UnitSymbol option

        member Count: int

        member Entries: (Dim * UnitSymbol) array

    [<RequireQualifiedAccess>]
    module UnitRegistry =

        val empty: UnitRegistry

        val add:
          dim: Dim ->
            symbol: UnitSymbol -> registry: UnitRegistry -> UnitRegistry

        val ofList: entries: (Dim * UnitSymbol) list -> UnitRegistry

        val ofSeq: entries: (Dim * UnitSymbol) seq -> UnitRegistry

        val toList: registry: UnitRegistry -> (Dim * UnitSymbol) list

        val merge: a: UnitRegistry -> b: UnitRegistry -> UnitRegistry

        val tryCanonical:
          registry: UnitRegistry -> dim: Dim -> UnitSymbol option

        val contains: registry: UnitRegistry -> dim: Dim -> bool

        val si: UnitRegistry

        val withPhotometry: registry: UnitRegistry -> UnitRegistry

namespace Marey

    type Prefix =
        {

          Name: string

          Symbol: string

          Ascii: string

          Latex: string

          Siunitx: string

          Exponent: int
        }

        member Factor: float

    [<RequireQualifiedAccess>]
    module Prefix =

        val none: Prefix

        val quecto: Prefix

        val ronto: Prefix

        val yocto: Prefix

        val zepto: Prefix

        val atto: Prefix

        val femto: Prefix

        val pico: Prefix

        val nano: Prefix

        val micro: Prefix

        val milli: Prefix

        val centi: Prefix

        val deci: Prefix

        val deca: Prefix

        val hecto: Prefix

        val kilo: Prefix

        val mega: Prefix

        val giga: Prefix

        val tera: Prefix

        val peta: Prefix

        val exa: Prefix

        val zetta: Prefix

        val yotta: Prefix

        val ronna: Prefix

        val quetta: Prefix

        val all: Prefix array

        val engineering: Prefix array

        val factor: p: Prefix -> float

        val ofExponent: e: int -> Prefix option

        val shift: k: int -> p: Prefix -> Prefix option

        val select: lo: float * hi: float -> Prefix

        val selectFor: dim: Dim -> float * float -> Prefix

        val selectForUnit: u: U<'u> -> float * float -> Prefix

namespace Marey

    type UnitStyle =

        | Unicode

        | Ascii

        | Latex

        | Siunitx

    [<RequireQualifiedAccess>]
    module UnitDisplay =

        val render:
          style: UnitStyle -> registry: UnitRegistry -> dim: Dim -> string

        val renderExpanded: style: UnitStyle -> dim: Dim -> string

        val renderSi: style: UnitStyle -> dim: Dim -> string

        val renderPrefixed:
          style: UnitStyle ->
            registry: UnitRegistry -> p: Prefix -> dim: Dim -> string

        val unitText:
          style: UnitStyle ->
            registry: UnitRegistry -> p: Prefix -> u: U<'u> -> string

        val withUnit: unitText: string -> shown: string -> string

        val quantityIn:
          style: UnitStyle ->
            registry: UnitRegistry ->
            prefix: Prefix ->
            number: (float -> string) -> u: U<'u> -> v: float<'u> -> string

        val quantity:
          style: UnitStyle ->
            registry: UnitRegistry ->
            prefixed: bool ->
            number: (float -> string) -> u: U<'u> -> v: float<'u> -> string

namespace Marey

    type AxisSpec =
        {

          Title: string

          Label: string

          UnitText: string

          TitleTex: string option

          UnitTextTex: string option

          Prefix: Prefix

          Ticks: TickLabel array

          Minor: float<nd> array
        }

    type TickSpec =

        | TicksAuto

        | TicksEvery of step: float

        | TicksAt of values: float array

        | TicksNamed of ticks: (float * string) array

    type MinorSpec =

        | MinorAuto
        | MinorOff

        | MinorEvery of step: float

        | MinorCount of count: int

    [<Sealed>]
    type TickFormatter =

        new: format: (float -> string) -> TickFormatter

        member Format: value: float -> string

        override ToString: unit -> string

    [<NoComparison>]
    type TickFormat =

        | FormatAuto

        | FormatFixed of decimals: int

        | FormatPercent

        | FormatScientific

        | FormatCustom of TickFormatter

    [<RequireQualifiedAccess;
      CompilationRepresentation (enum<CompilationRepresentationFlags> (4))>]
    module TickFormat =

        val custom: format: (float -> string) -> TickFormat

    [<RequireQualifiedAccess>]
    module Axis =

        [<Literal>]
        val DefaultTickCount: int = 5

        val title: label: string -> unitText: string -> string

        val auto:
          style: UnitStyle -> label: string -> scale: Scale<'u> -> AxisSpec

namespace Marey

    [<AutoOpen>]
    module UnitParsing =

        [<RequireQualifiedAccess>]
        module U =

            val tryParse: text: string -> Result<U<1>,string>

            val parse: text: string -> U<1>

            val tryAs: target: U<'u> -> u: IUnit -> U<'u> option

namespace Marey.CSharp

    [<AbstractClass; Sealed; Class>]
    type Units =

        static member Cube: a: Marey.IUnit -> Marey.U<1>

        static member Div: a: Marey.IUnit * b: Marey.IUnit -> Marey.U<1>

        static member Mul: a: Marey.IUnit * b: Marey.IUnit -> Marey.U<1>

        static member Parse: text: string -> Marey.U<1>

        static member Recip: a: Marey.IUnit -> Marey.U<1>

        static member
          Scaled: symbol: string * factor: float * a: Marey.IUnit -> Marey.U<1>

        static member Square: a: Marey.IUnit -> Marey.U<1>

        static member
          TryParse: text: string *
                    [<System.Runtime.InteropServices.Out>] result: byref<Marey.U<1>> ->
                      bool

        static member Ampere: Marey.U<Marey.A>

        static member Angstrom: Marey.U<Marey.angstrom>

        static member AstronomicalUnit: Marey.U<Marey.au>

        static member Atmosphere: Marey.U<Marey.atm>

        static member Bar: Marey.U<Marey.bar>

        static member Barn: Marey.U<Marey.barn>

        static member Becquerel: Marey.U<Marey.Bq>

        static member Calorie: Marey.U<Marey.cal>

        static member Candela: Marey.U<Marey.cd>

        static member Coulomb: Marey.U<Marey.C>

        static member CubicMetre: Marey.U<Marey.m ^ 3>

        static member Dalton: Marey.U<Marey.Da>

        static member Day: Marey.U<Marey.d>

        static member DegreeCelsius: Marey.U<Marey.degC>

        static member DegreeFahrenheit: Marey.U<Marey.degF>

        static member Electronvolt: Marey.U<Marey.eV>

        static member Farad: Marey.U<Marey.F>

        static member Gauss: Marey.U<Marey.Gs>

        static member Gram: Marey.U<Marey.kg>

        static member Gray: Marey.U<Marey.Gy>

        static member Henry: Marey.U<Marey.H>

        static member Hertz: Marey.U<Marey.Hz>

        static member Hour: Marey.U<Marey.h>

        static member Joule: Marey.U<Marey.J>

        static member Katal: Marey.U<Marey.kat>

        static member Kelvin: Marey.U<Marey.K>

        static member Kilogram: Marey.U<Marey.kg>

        static member KilogramPerCubicMetre: Marey.U<Marey.kg/Marey.m ^ 3>

        static member LightYear: Marey.U<Marey.ly>

        static member Litre: Marey.U<Marey.L>

        static member Lumen: Marey.U<Marey.lm>

        static member Lux: Marey.U<Marey.lx>

        static member Metre: Marey.U<Marey.m>

        static member MetrePerSecond: Marey.U<Marey.m/Marey.s>

        static member MetrePerSecondSquared: Marey.U<Marey.m/Marey.s ^ 2>

        static member Minute: Marey.U<Marey.minute>

        static member Mole: Marey.U<Marey.mol>

        static member Newton: Marey.U<Marey.N>

        static member Ohm: Marey.U<Marey.ohm>

        static member One: Marey.U<1>

        static member Parsec: Marey.U<Marey.pc>

        static member Pascal: Marey.U<Marey.Pa>

        static member PerSecond: Marey.U</Marey.s>

        static member Radian: Marey.U<Marey.rad>

        static member Second: Marey.U<Marey.s>

        static member Siemens: Marey.U<Marey.S>

        static member Sievert: Marey.U<Marey.Sv>

        static member SquareMetre: Marey.U<Marey.m ^ 2>

        static member Steradian: Marey.U<Marey.sr>

        static member Tesla: Marey.U<Marey.T>

        static member Unspecified: Marey.U<1>

        static member Volt: Marey.U<Marey.V>

        static member Watt: Marey.U<Marey.W>

        static member Weber: Marey.U<Marey.Wb>

        static member Year: Marey.U<Marey.yr>
